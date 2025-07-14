using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// This class ensures that we can link metadata about any unity asset both at edit and runtime.
/// </summary>
/// <remarks>
/// There's a caveat though, we have to rename assets with an added guid to map them correctly at runtime.
/// <br/>There are no other way that don't have worse caveat, for example, one way would be to have a LazyLoadReference pointing back to the asset from the metadata,
/// but lazy load is only effective at edit time, at runtime the asset is loaded in like any other references.
/// <br/>We could also include metadata through a AddObjectToAsset call between them and the asset,
/// but this is not supported for non-scriptable object assets, making this a poor solution.
/// </remarks>
[CreateAssetMenu]
public class MetadataTable : ScriptableObject
{
    private const string TagStart = "_guid:";

    static MetadataTable? __instance;

    public static MetadataTable Instance
    {
        get
        {
            __instance ??= Resources.Load<MetadataTable>(nameof(MetadataTable));
#if UNITY_EDITOR
            if (__instance == null)
            {
                __instance = CreateInstance<MetadataTable>();
                if (UnityEditor.AssetDatabase.IsValidFolder("Assets/Resources") == false)
                    UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");
                UnityEditor.AssetDatabase.CreateAsset(__instance, $"Assets/Resources/{nameof(MetadataTable)}.asset");
                UnityEditor.AssetDatabase.SaveAssets();
            }
#endif

            return __instance;
        }
    }

    [SerializeField] private List<Link> _list = new();
    private Dictionary<int, List<LazyLoadReference<CustomAssetMetadata>>>? _objectToMetada;

    public T? TryGet<T>(Object obj) where T : CustomAssetMetadata
    {
        if (_objectToMetada is null)
        {
            _objectToMetada = new();
            foreach (var link in _list)
            {
                if (_objectToMetada.TryGetValue(link.TargetId, out var list) == false)
                    _objectToMetada[link.TargetId] = list = new();
                list.Add(link.Metadata);
            }
        }

        if (_objectToMetada.TryGetValue(GetIdFrom(obj), out var dataForThiSObj))
            return dataForThiSObj.FirstOrDefault(x => x.asset is T).asset as T;

        return null;
    }

    public IEnumerable<CustomAssetMetadata> GetAll(Object obj)
    {
        if (_objectToMetada is null)
        {
            _objectToMetada = new();
            foreach (var link in _list)
            {
                if (_objectToMetada.TryGetValue(link.TargetId, out var list) == false)
                    _objectToMetada[link.TargetId] = list = new();
                list.Add(link.Metadata);
            }
        }

        if (_objectToMetada.TryGetValue(GetIdFrom(obj), out var dataForThiSObj))
            return dataForThiSObj.Select(x => x.asset);

        return Enumerable.Empty<CustomAssetMetadata>();
    }

    int GetIdFrom(Object obj)
    {
        #if UNITY_EDITOR
        return obj.GetInstanceID();
        #else
        var indexOfId = obj.name.IndexOf(TagStart, StringComparison.Ordinal);
        if (indexOfId != -1)
        {
            ReadOnlySpan<char> span = obj.name.AsSpan()[(indexOfId + TagStart.Length)..];
            Debug.LogError(span.ToString());
            return int.Parse(span);
        }
        else
        {
            return 0;
        }
        #endif
    }

    [Serializable]
    public struct Link
    {
#if UNITY_EDITOR
        public LazyLoadReference<Object> _reference;
#endif

        public int _associatedId;

        public LazyLoadReference<CustomAssetMetadata> Metadata;

        public int TargetId
        {
            get
            {
                #if UNITY_EDITOR
                return _reference.instanceID;
                #else
                return _associatedId;
                #endif
            }
        }
    }

    #if UNITY_EDITOR
    internal CustomAssetMetadata CreateAndAdd(Object target, Type metadataType)
    {
        var instance = (CustomAssetMetadata)CreateInstance(metadataType);
        instance.name = metadataType.Name;
        UnityEditor.AssetDatabase.AddObjectToAsset(instance, this);
        
        _list.Add(new Link { _reference = target, Metadata = instance });
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
        UnityEditor.AssetDatabase.Refresh();
        if (_objectToMetada is not null)
        {
            int id = target.GetInstanceID();
            if (_objectToMetada.TryGetValue(id, out var list) == false)
                _objectToMetada[id] = list = new();
            list.Add(instance);
        }

        return instance;
    }

    internal void Destroy(CustomAssetMetadata metadata)
    {
        for (var i = _list.Count - 1; i >= 0; i--)
        {
            if (_list[i].Metadata.instanceID == metadata.GetInstanceID())
            {
                if (_objectToMetada != null && _objectToMetada.TryGetValue(_list[i].TargetId, out var list))
                {
                    for (int j = list.Count - 1; j >= 0; j--)
                    {
                        if (list[j].instanceID == metadata.GetInstanceID())
                            list.RemoveAt(j);
                    }
                }
                _list.RemoveAt(i);
            }
        }

        UnityEditor.AssetDatabase.RemoveObjectFromAsset(metadata);
        UnityEngine.Object.DestroyImmediate(metadata);
        UnityEditor.AssetDatabase.Refresh();
    }

    public class BuildProcessor : UnityEditor.Build.IPreprocessBuildWithReport, UnityEditor.Build.IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            var instance = Resources.Load<MetadataTable>(nameof(MetadataTable));
            if (instance == null)
                return;

            for (int i = 0; i < instance._list.Count; i++)
            {
                var metadata = instance._list[i];
                var asset = metadata._reference.asset;
                var id = asset.GetInstanceID();

                metadata._associatedId = id;
                instance._list[i] = metadata;

                asset.name += $"{TagStart}{id}";
                Debug.Log(asset.name);

                UnityEditor.EditorUtility.SetDirty(asset);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
                UnityEditor.EditorUtility.SetDirty(instance);
            }
            UnityEditor.AssetDatabase.SaveAssetIfDirty(instance);
            UnityEditor.AssetDatabase.Refresh();
        }

        public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            var instance = Resources.Load<MetadataTable>(nameof(MetadataTable));
            if (instance == null)
                return;

            foreach (var metadata in instance._list)
            {
                var asset = metadata._reference.asset;
                asset.name = asset.name[..asset.name.IndexOf(TagStart, StringComparison.Ordinal)];
                UnityEditor.EditorUtility.SetDirty(asset);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
            }
            UnityEditor.AssetDatabase.Refresh();
        }
    }
    #endif
}
