// Firestore rules for the shared leaderboard, run against the emulator:
//   npm test   (firebase emulators:exec starts the emulator, then `node --test`)
// Covers test-vectors.json (also read by the C# mirror, SharedLeaderboardTest) and a few rules-only cases.
const { test, before, after, beforeEach } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const { initializeTestEnvironment, assertSucceeds, assertFails } = require('@firebase/rules-unit-testing');
const {
  doc, setDoc, getDoc, getDocs, deleteDoc, collection, query, orderBy, limit, serverTimestamp, Timestamp,
} = require('firebase/firestore');

const vectors = JSON.parse(fs.readFileSync(path.join(__dirname, 'test-vectors.json'), 'utf8')).cases;
const OWNER = 'uid-owner';
const PROVIDERS = { android: 'playgames.google.com', ios: 'gc.apple.com' };
const asUser = (uid, provider) => env.authenticatedContext(uid, { firebase: { sign_in_provider: provider, identities: {} } }).firestore();

let env;

before(async () => {
  env = await initializeTestEnvironment({
    projectId: 'demo-pause-shared',
    firestore: { rules: fs.readFileSync(path.join(__dirname, 'firestore.rules'), 'utf8') },
  });
});

after(async () => { await env.cleanup(); });

beforeEach(async () => { await env.clearFirestore(); });

async function seed(table, uid, data, secondsAgo) {
  await env.withSecurityRulesDisabled(async (ctx) => {
    await setDoc(doc(ctx.firestore(), table, uid), {
      ...data,
      updatedAt: Timestamp.fromMillis(Date.now() - secondsAgo * 1000),
    });
  });
}

for (const v of vectors) {
  test(`${v.name} [${v.table} ${v.op}]`, async () => {
    if (v.hasExisting) await seed(v.table, OWNER, v.existing, v.sinceLastSeconds);
    const db = asUser(v.owner ? OWNER : 'uid-other', v.provider || PROVIDERS[v.doc.platform] || 'custom');
    const write = setDoc(doc(db, v.table, OWNER), { ...v.doc, updatedAt: serverTimestamp() });
    if (v.expect === 'allow') await assertSucceeds(write); else await assertFails(write);
  });
}

test('a string or fractional score is refused', async () => {
  const db = asUser(OWNER, 'playgames.google.com');
  const withScore = (score) => ({
    ...vectors[0].doc,
    score,
    updatedAt: serverTimestamp(),
  });
  await assertFails(setDoc(doc(db, 'main', OWNER), withScore('1000')));
  await assertFails(setDoc(doc(db, 'main', OWNER), withScore(1000.5)));
});

test('a client-chosen timestamp is refused', async () => {
  const db = asUser(OWNER, 'playgames.google.com');
  await assertFails(setDoc(doc(db, 'main', OWNER), { ...vectors[0].doc, updatedAt: Timestamp.fromMillis(Date.now() - 100000) }));
});

test('boards are publicly readable and writes need sign-in', async () => {
  await seed('main', OWNER, vectors[0].doc, 100);
  await seed('power', OWNER, vectors[1].doc, 100);
  const anon = env.unauthenticatedContext().firestore();
  await assertSucceeds(getDoc(doc(anon, 'main', OWNER)));
  await assertSucceeds(getDoc(doc(anon, 'power', OWNER)));
  await assertSucceeds(getDoc(doc(env.authenticatedContext('uid-other').firestore(), 'main', OWNER)));
  await assertFails(setDoc(doc(anon, 'main', OWNER), { ...vectors[0].doc, updatedAt: serverTimestamp() }));
  await assertFails(deleteDoc(doc(anon, 'main', OWNER)));
});

test('a list needs a limit of 25 or less, signed in or not', async () => {
  await seed('main', OWNER, vectors[0].doc, 100);
  await seed('power', OWNER, vectors[1].doc, 100);
  for (const db of [env.unauthenticatedContext().firestore(), env.authenticatedContext('uid-other').firestore()]) {
    for (const table of ['main', 'power']) {
      const top = (n) => query(collection(db, table), orderBy('score', 'desc'), limit(n));
      await assertSucceeds(getDocs(top(10)));
      await assertSucceeds(getDocs(top(25)));
      await assertFails(getDocs(top(26)));
      await assertFails(getDocs(query(collection(db, table), orderBy('score', 'desc'))));
    }
  }
});

test('the owner deletes their doc, others cannot', async () => {
  await seed('main', OWNER, vectors[0].doc, 100);
  await assertFails(deleteDoc(doc(env.authenticatedContext('uid-other').firestore(), 'main', OWNER)));
  await assertSucceeds(deleteDoc(doc(env.authenticatedContext(OWNER).firestore(), 'main', OWNER)));
});

test('other collections are closed', async () => {
  const db = asUser(OWNER, 'playgames.google.com');
  await assertFails(setDoc(doc(db, 'scores', OWNER), { a: 1 }));
  await assertFails(getDoc(doc(db, 'scores', OWNER)));
});

test('every tab orders the same documents: score, dust and worlds are top-level, ties fall back to the score', async () => {
  await seed('main', 'a', { ...vectors[0].doc, worlds: 0 }, 100);
  await seed('main', 'b', { ...vectors[0].doc, score: 50, dust: 0, worlds: 0 }, 100);
  const anon = env.unauthenticatedContext().firestore();
  for (const field of ['score', 'dust', 'worlds']) {
    const q = field === 'score'
      ? query(collection(anon, 'main'), orderBy('score', 'desc'), limit(10))
      : query(collection(anon, 'main'), orderBy(field, 'desc'), orderBy('score', 'desc'), limit(10));
    const snap = await assertSucceeds(getDocs(q));
    if (snap.size !== 2) throw new Error(`${field} tab lists ${snap.size} players, expected both`);
  }
});

test('an old-shape document is readable by anyone', async () => {
  const old = vectors.find((v) => v.name.startsWith('create denied: a new doc in the old per-column shape')).doc;
  await seed('main', OWNER, old, 100);
  await assertSucceeds(getDoc(doc(env.unauthenticatedContext().firestore(), 'main', OWNER)));
});
