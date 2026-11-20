import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import mapping as M  # noqa: E402


class AchievementMapping(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows = M.load_achievements()

    def test_count_and_points(self):
        self.assertEqual(len(self.rows), 68)
        self.assertEqual(sum(r["points"] for r in self.rows), 1000)

    def test_no_problems_all_images_exist(self):
        self.assertEqual(M.validate_achievements(self.rows), [])

    def test_hidden_flag_inverts(self):
        hidden = [r["vendorIdentifier"].split(".ach_")[1] for r in self.rows if not r["showBeforeEarned"]]
        self.assertEqual(sorted(hidden), ["deaths_100", "mega_domino", "pause_perfect_dodge"])

    def test_texts_updated(self):
        by = {r["vendorIdentifier"].split(".ach_")[1]: r for r in self.rows}
        self.assertEqual(by["elite_verdant_all"]["afterEarnedDescription"], "Down every Verdant elite.")
        self.assertIn("5000", by["score_10k"]["beforeEarnedDescription"])
        self.assertIn("20000", by["score_50k"]["beforeEarnedDescription"])
        self.assertIn("80000", by["score_150k"]["beforeEarnedDescription"])

    def test_attrs_shape(self):
        a = M.achievement_attrs(self.rows[0])
        self.assertEqual(set(a), {"referenceName", "points", "showBeforeEarned", "repeatable"})
        self.assertEqual(M.achievement_loc_attrs(self.rows[0])["locale"], "en-US")

    def test_validation_catches_overflow(self):
        bad = [dict(self.rows[0], points=101)]
        self.assertTrue(any("out of" in p for p in M.validate_achievements(bad)))


class Leaderboard(unittest.TestCase):
    def test_top_score(self):
        b = M.top_score_board()
        self.assertEqual(b["attrs"]["vendorIdentifier"], "me.hapticgate.pause.top_score")
        self.assertEqual(b["attrs"]["scoreSortType"], "DESC")
        self.assertEqual(b["attrs"]["submissionType"], "BEST_SCORE")
        self.assertEqual(b["attrs"]["defaultFormatter"], "INTEGER_COMMA_SEPARATOR")


class Iap(unittest.TestCase):
    def test_products_valid(self):
        locale, products = M.load_iap()
        self.assertEqual(locale, "en-US")
        self.assertEqual(M.validate_iap(products), [])
        self.assertEqual(len(products), 5)

    def test_attrs(self):
        _, products = M.load_iap()
        a = M.iap_attrs(products[0])
        self.assertEqual(a["inAppPurchaseType"], "NON_CONSUMABLE")
        self.assertEqual(a["productId"], "me.hapticgate.pause.ship_paragon")

    def test_length_validation(self):
        p = dict(M.load_iap()[1][0], displayName="x" * 31)
        self.assertTrue(M.validate_iap([p]))


if __name__ == "__main__":
    unittest.main()
