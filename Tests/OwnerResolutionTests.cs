using System.Collections.Generic;
using Xunit;

namespace CompanyBrandFix.Tests
{
    public class OwnerResolutionTests
    {
        private sealed class City
        {
            internal readonly HashSet<int> Entities = new HashSet<int> { 1 };
            internal readonly HashSet<int> Buildings = new HashSet<int>();
            internal readonly Dictionary<int, int> Owners = new Dictionary<int, int>();
            internal int OwnerReads;

            internal OwnerResolution<int> Resolve()
            {
                return RepairDecisions.ResolveOwner(1, 0, Entities.Contains,
                    entity =>
                    {
                        OwnerReads++;
                        return Owners.TryGetValue(entity, out int owner) ? owner : 0;
                    }, Buildings.Contains);
            }
        }

        [Fact]
        public void PropWithoutOwnerIsStandalone()
        {
            var result = new City().Resolve();
            Assert.False(result.HasOwner);
            Assert.Equal(0, result.Building);
        }

        [Fact]
        public void ExplicitNullOwnerIsStandalone()
        {
            var city = new City();
            city.Owners[1] = 0;
            Assert.False(city.Resolve().HasOwner);
        }

        [Fact]
        public void NestedBuildingOwnershipFindsTheBuildingToRefresh()
        {
            var city = new City();
            city.Entities.UnionWith(new[] { 2, 3 });
            city.Owners[1] = 2;
            city.Owners[2] = 3;
            city.Buildings.Add(3);

            var result = city.Resolve();
            Assert.True(result.HasOwner);
            Assert.Equal(3, result.Building);
            Assert.Equal(3, result.LastOwner);
        }

        [Fact]
        public void NonBuildingOwnerIsNotMistakenForStandalone()
        {
            var city = new City();
            city.Entities.Add(2);
            city.Owners[1] = 2;

            var result = city.Resolve();
            Assert.True(result.HasOwner);
            Assert.Equal(0, result.Building);
            Assert.Equal(2, result.LastOwner);
        }

        [Fact]
        public void MissingOwnerIsSkippedEvenIfItsOldIdWasABuilding()
        {
            var city = new City();
            city.Owners[1] = 2;
            city.Buildings.Add(2);

            var result = city.Resolve();
            Assert.True(result.HasOwner);
            Assert.Equal(0, result.Building);
            Assert.Equal(2, result.LastOwner);
            Assert.Equal(1, city.OwnerReads);
        }

        [Fact]
        public void SelfOwnedPropIsSkippedRatherThanDeletedAsStandalone()
        {
            var city = new City();
            city.Owners[1] = 1;

            var result = city.Resolve();
            Assert.True(result.HasOwner);
            Assert.Equal(0, result.Building);
            Assert.Equal(1, city.OwnerReads);
        }

        [Fact]
        public void OwnershipCycleIsBoundedAndSkipped()
        {
            var city = new City();
            city.Entities.Add(2);
            city.Owners[1] = 2;
            city.Owners[2] = 1;

            var result = city.Resolve();
            Assert.True(result.HasOwner);
            Assert.Equal(0, result.Building);
            Assert.InRange(city.OwnerReads, 1, 16);
        }

        [Theory]
        [InlineData(16, true)]
        [InlineData(17, false)]
        public void BuildingAtTraversalLimitIsFoundButDeeperBuildingIsSkipped(
            int links, bool shouldFindBuilding)
        {
            var city = new City();
            for (int entity = 1; entity <= links; entity++)
            {
                city.Entities.Add(entity + 1);
                city.Owners[entity] = entity + 1;
            }
            city.Buildings.Add(links + 1);

            var result = city.Resolve();
            Assert.True(result.HasOwner);
            Assert.Equal(shouldFindBuilding ? links + 1 : 0, result.Building);
            Assert.Equal(16, city.OwnerReads);
        }
    }
}
