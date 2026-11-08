using NUnit.Framework;

public class CodexCollectScrollTest
{
    [Test]
    public void ScrollTargetSitsJustUnderThePinnedHeader()
    {
        Assert.AreEqual(500f - 40f - CodexPanel.GridInset, CodexPanel.CardScrollY(500f, 40f, 2000f), .001f);
    }

    [Test]
    public void ScrollTargetStaysInsideTheList()
    {
        Assert.AreEqual(0f, CodexPanel.CardScrollY(10f, 40f, 2000f), .001f);
        Assert.AreEqual(300f, CodexPanel.CardScrollY(5000f, 40f, 300f), .001f);
        Assert.AreEqual(0f, CodexPanel.CardScrollY(500f, 40f, 0f), .001f);
    }
}

public class CodexGlideTest
{
    [Test]
    public void GlideTargetIsClampedToTheList()
    {
        Assert.AreEqual(100f, CodexGlide.TargetY(100f, 0f, 500f), .001f);
        Assert.AreEqual(0f, CodexGlide.TargetY(5f, 8f, 500f), .001f);
        Assert.AreEqual(500f, CodexGlide.TargetY(900f, 0f, 500f), .001f);
    }
}
