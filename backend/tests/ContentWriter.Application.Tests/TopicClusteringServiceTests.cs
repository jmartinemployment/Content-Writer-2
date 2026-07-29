using ContentWriter.Application.Services;

namespace ContentWriter.Application.Tests;

public class TopicClusteringServiceTests
{
    [Fact]
    public void ClusterKeywordList_groups_phrases_sharing_the_same_top_significant_words_into_one_cluster()
    {
        // Clustering keys on the top-3 alphabetically-sorted significant (>3 char, non-stopword)
        // tokens in each phrase — these two share "accounts"/"automation"/"payable" as that top-3
        // set once "software"/"tools" are cut off by the alphabetical top-3 truncation.
        var keywords = new List<string>
        {
            "accounts payable automation software",
            "accounts payable automation tools",
            "cash flow forecasting tools",
        };

        var clusters = TopicClusteringService.ClusterKeywordList(keywords);

        Assert.Equal(2, clusters.Count);
        var apCluster = Assert.Single(clusters, c => c.Keywords.Any(k => k.Contains("accounts payable")));
        Assert.Equal(2, apCluster.Keywords.Count);
    }

    [Fact]
    public void ClusterKeywordList_deduplicates_and_ignores_blank_entries()
    {
        var keywords = new List<string> { "cash flow forecasting", "cash flow forecasting", "", "   " };

        var clusters = TopicClusteringService.ClusterKeywordList(keywords);

        Assert.Single(clusters);
        Assert.Single(clusters[0].Keywords);
    }

    [Fact]
    public void ClusterKeywordList_returns_empty_for_empty_input()
    {
        var clusters = TopicClusteringService.ClusterKeywordList([]);

        Assert.Empty(clusters);
    }
}
