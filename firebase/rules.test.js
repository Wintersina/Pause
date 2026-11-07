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
    const db = env.authenticatedContext(v.owner ? OWNER : 'uid-other').firestore();
    const write = setDoc(doc(db, v.table, OWNER), { ...v.doc, updatedAt: serverTimestamp() });
    if (v.expect === 'allow') await assertSucceeds(write); else await assertFails(write);
  });
}

test('a string or fractional score is refused', async () => {
  const db = env.authenticatedContext(OWNER).firestore();
  const withScore = (score) => ({
    ...vectors[0].doc,
    byScore: { ...vectors[0].doc.byScore, score },
    updatedAt: serverTimestamp(),
  });
  await assertFails(setDoc(doc(db, 'main', OWNER), withScore('1000')));
  await assertFails(setDoc(doc(db, 'main', OWNER), withScore(1000.5)));
});

test('a client-chosen timestamp is refused', async () => {
  const db = env.authenticatedContext(OWNER).firestore();
  await assertFails(setDoc(doc(db, 'main', OWNER), { ...vectors[0].doc, updatedAt: Timestamp.fromMillis(Date.now() - 100000) }));
});

test('reads need sign-in', async () => {
  await seed('main', OWNER, vectors[0].doc, 100);
  await assertFails(getDoc(doc(env.unauthenticatedContext().firestore(), 'main', OWNER)));
  await assertSucceeds(getDoc(doc(env.authenticatedContext('uid-other').firestore(), 'main', OWNER)));
});

test('a list needs a limit of 25 or less', async () => {
  await seed('main', OWNER, vectors[0].doc, 100);
  const db = env.authenticatedContext('uid-other').firestore();
  const top = (n) => query(collection(db, 'main'), orderBy('byScore.score', 'desc'), limit(n));
  await assertSucceeds(getDocs(top(10)));
  await assertSucceeds(getDocs(top(25)));
  await assertFails(getDocs(top(26)));
  await assertFails(getDocs(query(collection(db, 'main'), orderBy('byScore.score', 'desc'))));
});

test('the owner deletes their doc, others cannot', async () => {
  await seed('main', OWNER, vectors[0].doc, 100);
  await assertFails(deleteDoc(doc(env.authenticatedContext('uid-other').firestore(), 'main', OWNER)));
  await assertSucceeds(deleteDoc(doc(env.authenticatedContext(OWNER).firestore(), 'main', OWNER)));
});

test('other collections are closed', async () => {
  const db = env.authenticatedContext(OWNER).firestore();
  await assertFails(setDoc(doc(db, 'scores', OWNER), { a: 1 }));
  await assertFails(getDoc(doc(db, 'scores', OWNER)));
});
