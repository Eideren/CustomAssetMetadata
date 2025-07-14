using System.Runtime.CompilerServices;

public static class GetMetadataOfTypeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Metadata? TryGetMetadata<Metadata>(this UnityEngine.Object asset)
        where Metadata : CustomAssetMetadata
    {
        return MetadataTable.Instance.TryGet<Metadata>(asset);
    }
}
