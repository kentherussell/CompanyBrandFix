using System;
using System.Collections.Generic;
using Xunit;

namespace CompanyBrandFix.Tests
{
    public class BrandSelectionTests
    {
        [Fact]
        public void EmptyCompatibleListSkipsRepairWithoutReadingAnyPrefab()
        {
            Assert.Equal(-1, RepairDecisions.FindCompatibleBrandIndex(7, 0,
                _ => throw new InvalidOperationException("No prefab should be read.")));
        }

        [Fact]
        public void AllInvalidBrandsSkipRepairAfterCheckingEachCandidateOnce()
        {
            var visited = new List<int>();
            int result = RepairDecisions.FindCompatibleBrandIndex(2, 4, index =>
            {
                visited.Add(index);
                return false;
            });

            Assert.Equal(-1, result);
            Assert.Equal(new[] { 2, 3, 0, 1 }, visited);
        }

        [Fact]
        public void SelectionWrapsPastInvalidBrandsAndStopsAtFirstValidBrand()
        {
            var visited = new List<int>();
            int result = RepairDecisions.FindCompatibleBrandIndex(3, 4, index =>
            {
                visited.Add(index);
                return index == 1 || index == 2;
            });

            Assert.Equal(1, result);
            Assert.Equal(new[] { 3, 0, 1 }, visited);
        }

        [Fact]
        public void HealthyBrandsAreDistributedDeterministicallyAcrossCompanies()
        {
            for (int company = 0; company < 12; company++)
            {
                int first = RepairDecisions.FindCompatibleBrandIndex(company, 4, _ => true);
                int second = RepairDecisions.FindCompatibleBrandIndex(company, 4, _ => true);
                Assert.Equal(company % 4, first);
                Assert.Equal(first, second);
            }
        }

        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void ExtremeCompanyIndicesStillSelectAnAvailableBrand(int company)
        {
            Assert.Equal(2, RepairDecisions.FindCompatibleBrandIndex(company, 3, i => i == 2));
        }

        [Fact]
        public void EverySmallValidityCombinationFindsAValidBrandOrSkipsRepair()
        {
            // Exhaust all subsets, including the empty subset, rather than
            // relying only on one hand-picked position for a healthy brand.
            for (int count = 1; count <= 6; count++)
            for (int mask = 0; mask < (1 << count); mask++)
            for (int company = 0; company < count; company++)
            {
                var visited = new HashSet<int>();
                int result = RepairDecisions.FindCompatibleBrandIndex(company, count, i =>
                {
                    Assert.InRange(i, 0, count - 1);
                    Assert.True(visited.Add(i), "A candidate was checked twice.");
                    return (mask & (1 << i)) != 0;
                });

                if (mask == 0)
                {
                    Assert.Equal(-1, result);
                    Assert.Equal(count, visited.Count);
                }
                else
                {
                    Assert.InRange(result, 0, count - 1);
                    Assert.NotEqual(0, mask & (1 << result));
                }
            }
        }
    }
}
