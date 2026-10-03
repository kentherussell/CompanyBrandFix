using System;

namespace CompanyBrandFix
{
    // These decisions do not require a running ECS world. The systems supply
    // prefab validity and ownership from the game; tests supply small fixtures.
    internal static class RepairDecisions
    {
        internal const int MaxOwnerDepth = 16;

        internal static int FindCompatibleBrandIndex(
            int companyIndex, int count, Func<int, bool> isValid)
        {
            if (count == 0)
            {
                return -1;
            }

            int startIndex = (companyIndex & int.MaxValue) % count;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (startIndex + offset) % count;
                if (isValid(index))
                {
                    return index;
                }
            }

            return -1;
        }

        internal static OwnerResolution<T> ResolveOwner<T>(
            T prop, T nullEntity, Func<T, bool> exists,
            Func<T, T> getOwner, Func<T, bool> isBuilding)
            where T : struct, IEquatable<T>
        {
            T current = prop;
            T lastOwner = nullEntity;
            bool hasOwner = false;

            for (int depth = 0; depth < MaxOwnerDepth; depth++)
            {
                if (!exists(current))
                {
                    break;
                }

                T owner = getOwner(current);
                if (owner.Equals(nullEntity))
                {
                    break;
                }

                // Even malformed ownership means this is not a standalone prop.
                hasOwner = true;
                lastOwner = owner;
                if (owner.Equals(current) || !exists(owner))
                {
                    break;
                }

                if (isBuilding(owner))
                {
                    return new OwnerResolution<T>(true, owner, owner);
                }

                current = owner;
            }

            return new OwnerResolution<T>(hasOwner, nullEntity, lastOwner);
        }
    }

    internal readonly struct OwnerResolution<T>
    {
        internal readonly bool HasOwner;
        internal readonly T Building;
        internal readonly T LastOwner;

        internal OwnerResolution(bool hasOwner, T building, T lastOwner)
        {
            HasOwner = hasOwner;
            Building = building;
            LastOwner = lastOwner;
        }
    }
}
