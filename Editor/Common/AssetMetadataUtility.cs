using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.ComponentModel;
using System.Linq;

public static class AssetMetadataUtility
{
	static HashSet<Type> disallowMultipleMetadataLookup;
	static Dictionary<Type, Type[]> restrictedTypesLookup;
	static Dictionary<Type, (string displayName, string menuPath)> metadataNames;
	static List<Type> allCustomAssetMetadataTypes;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void EnsureInitialized()
    {
        if (allCustomAssetMetadataTypes != null)
            return;

        var metadataTypes = TypeCache.GetTypesDerivedFrom<CustomAssetMetadata>();
		metadataNames = new Dictionary<Type, (string displayName, string menuPath)>(metadataTypes.Count);
		allCustomAssetMetadataTypes = metadataTypes.ToList();
        restrictedTypesLookup = new Dictionary<Type, Type[]>();
        disallowMultipleMetadataLookup = new HashSet<Type>();
		foreach (var metadataType in metadataTypes)
        {
            string displayName = null;
			string menuName = null;

			var displayNameAttribute = metadataType.GetCustomAttribute<DisplayNameAttribute>();
            if (displayNameAttribute != null)
            {
                displayName = displayNameAttribute.DisplayName;
            }

			var addMetadataMenuAttribute = metadataType.GetCustomAttribute<AddMetadataMenuAttribute>();
			if (addMetadataMenuAttribute != null)
			{
				menuName = addMetadataMenuAttribute.MetadataMenu.Replace('\\', '/');
			}
            
            if (displayName == null && menuName != null)
            {
                int index = menuName.LastIndexOf('/');
                displayName = (index == -1) ? menuName : menuName.Substring(index + 1);
			}

			if (displayName == null)
			{
                displayName = ObjectNames.NicifyVariableName(metadataType.Name);
			}

			if (menuName == null && displayName != null)
			{
				menuName = displayName;
			}

            metadataNames[metadataType] = (displayName, menuName);
			var disallowMultipleCustomAssetMetadataAttribute = metadataType.GetCustomAttribute<DisallowMultipleCustomAssetMetadataAttribute>();
            if (disallowMultipleCustomAssetMetadataAttribute != null)
            {
                disallowMultipleMetadataLookup.Add(metadataType);
			}
			var restrictMetadataAssetTypesAttribute = metadataType.GetCustomAttribute<RestrictMetadataAssetTypesAttribute>();
			if (restrictMetadataAssetTypesAttribute != null)
			{
				restrictedTypesLookup[metadataType] = restrictMetadataAssetTypesAttribute.Types;
			}
		}
    }

    public static bool HaveMetadataTypes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureInitialized();
            return allCustomAssetMetadataTypes.Count > 0;
        }
    }

    public static List<Type> AllMetadataTypes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureInitialized();
            return allCustomAssetMetadataTypes;
        }
	}

	public static string GetDisplayName(Type metadataType)
	{
		if (metadataType == null) throw new NullReferenceException(nameof(metadataType));
		EnsureInitialized();
		if (metadataNames.TryGetValue(metadataType, out var value))
			return value.displayName;
		return ObjectNames.NicifyVariableName(metadataType.Name);
	}

	public static string GetMenuName(Type metadataType)
	{
		if (metadataType == null) throw new NullReferenceException(nameof(metadataType));
		EnsureInitialized();
        if (metadataNames.TryGetValue(metadataType, out var value))
            return value.menuPath;
        return ObjectNames.NicifyVariableName(metadataType.Name);
	}

	public static void GetAll(UnityEngine.Object target, List<CustomAssetMetadata> metadata)
	{
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		if (metadata == null) throw new NullReferenceException(nameof(metadata));
		EnsureInitialized();
		var assetPath = AssetDatabase.GetAssetPath(target);
        if (assetPath == null || string.IsNullOrEmpty(assetPath))
            return;

        metadata.AddRange(MetadataTable.Instance.GetAll(target));
    }

    public static CustomAssetMetadata Add(UnityEngine.Object target, Type type)
	{
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		if (type == null) throw new NullReferenceException(nameof(type));
		EnsureInitialized();
		if (!CanAddMetadataType(target, type))
            return null;

		var assetPath = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrWhiteSpace(assetPath))
		{
			Debug.LogError("AssetMetadataUtility.Add: Could not find asset path for target", target);
			return null;
        }

        return MetadataTable.Instance.CreateAndAdd(target, type);
	}

	public static bool CanAddMetadataType(UnityEngine.Object target, Type type)
	{
		if (type == null) throw new NullReferenceException(nameof(type));
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		EnsureInitialized();
        if (disallowMultipleMetadataLookup.Contains(type))
        {
            if (MetadataTable.Instance.GetAll(target).Any(x => type.IsAssignableFrom(x.GetType())))
                return false;
        }
        if (!restrictedTypesLookup.TryGetValue(type, out var restrictedTypes))
			return true;

        var targetType = target.GetType();
		foreach (var restrictedType in restrictedTypes)
        {
            if (restrictedType == targetType)
                return true;
        }
        return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CustomAssetMetadata Add<Metadata>(UnityEngine.Object target)
        where Metadata : CustomAssetMetadata
	{
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		EnsureInitialized();
		return Add(target, typeof(Metadata));
    }

    public static void Destroy<Metadata>(Metadata metadata)
        where Metadata : CustomAssetMetadata
	{
		if (metadata == null || ReferenceEquals(metadata, null)) throw new NullReferenceException(nameof(metadata));
		EnsureInitialized();

        var assetPath = AssetDatabase.GetAssetPath(metadata);
        if (assetPath == null)
            return;

        MetadataTable.Instance.Destroy(metadata);
    }
}
