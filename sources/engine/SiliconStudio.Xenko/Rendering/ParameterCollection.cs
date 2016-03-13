using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SiliconStudio.Core;
using SiliconStudio.Core.Collections;
using SiliconStudio.Core.Extensions;
using SiliconStudio.Core.Serialization;

namespace SiliconStudio.Xenko.Rendering
{
    /// <summary>
    /// Manage several effect parameters (resources and data). A specific data and resource layout can be forced (usually by the consuming effect).
    /// </summary>
    [DataSerializer(typeof(ParameterCollection.Serializer))]
    [DataSerializerGlobal(null, typeof(FastList<ParameterKeyInfo>))]
    public class ParameterCollection
    {
        private static readonly byte[] EmptyData = new byte[0];

        private ParameterCollectionLayout layout;

        // TODO: Switch to FastListStruct (for serialization)
        private FastList<ParameterKeyInfo> parameterKeyInfos = new FastList<ParameterKeyInfo>(4);

        // Constants and resources
        // TODO: Currently stored in unmanaged array so we can get a pointer that can be updated from outside
        //   However, maybe ref locals would make this not needed anymore?
        [DataMemberIgnore]
        public byte[] DataValues = EmptyData;
        [DataMemberIgnore]
        public object[] ObjectValues;

        [DataMemberIgnore]
        public int PermutationCounter = 1;

        [DataMemberIgnore]
        public int LayoutCounter = 1;

        [DataMemberIgnore]
        public FastList<ParameterKeyInfo> ParameterKeyInfos => parameterKeyInfos;

        [DataMemberIgnore]
        public ParameterCollectionLayout Layout => layout;

        [DataMemberIgnore]
        public bool HasLayout => layout != null;

        public ParameterCollection()
        {
        }

        public unsafe ParameterCollection(ParameterCollection parameterCollection)
        {
            // Copy layout
            if (parameterCollection.layout != null)
            {
                layout = parameterCollection.layout;
            }

            // Copy parameter keys
            parameterKeyInfos.AddRange(parameterCollection.parameterKeyInfos);

            // Copy objects
            if (parameterCollection.ObjectValues != null)
            {
                ObjectValues = new object[parameterCollection.ObjectValues.Length];
                for (int i = 0; i < ObjectValues.Length; ++i)
                    ObjectValues[i] = parameterCollection.ObjectValues[i];
            }

            // Copy data
            if (parameterCollection.DataValues != null)
            {
                DataValues = new byte[parameterCollection.DataValues.Length];
                fixed (byte* dataValuesSources = parameterCollection.DataValues)
                fixed (byte* dataValuesDest = DataValues)
                {
                    Utilities.CopyMemory((IntPtr)dataValuesDest, (IntPtr)dataValuesSources, DataValues.Length);
                }
            }
        }

        /// <summary>
        /// Gets an accessor to get and set objects more quickly.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameterKey"></param>
        /// <returns></returns>
        public ObjectParameterAccessor<T> GetAccessor<T>(ObjectParameterKey<T> parameterKey, bool createIfNew = true)
        {
            var accessor = GetObjectParameterHelper(parameterKey, createIfNew);
            return new ObjectParameterAccessor<T>(accessor.Offset, accessor.Count);
        }

        /// <summary>
        /// Gets an accessor to get and set permutations more quickly.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameterKey"></param>
        /// <returns></returns>
        public PermutationParameter<T> GetAccessor<T>(PermutationParameterKey<T> parameterKey, bool createIfNew = true)
        {
            // Remap it as PermutationParameter
            var accessor = GetObjectParameterHelper(parameterKey, createIfNew);
            return new PermutationParameter<T>(accessor.Offset, accessor.Count);
        }

        /// <summary>
        /// Gets an accessor to get and set blittable values more quickly.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameterKey"></param>
        /// <returns></returns>
        public ValueParameter<T> GetAccessor<T>(ValueParameterKey<T> parameterKey, int elementCount = 1) where T : struct
        {
            var accessor = GetValueAccessorHelper(parameterKey, elementCount);
            return new ValueParameter<T>(accessor.Offset, accessor.Count);
        }

        private unsafe Accessor GetValueAccessorHelper(ParameterKey parameterKey, int elementCount = 1)
        {
            // Find existing first
            for (int i = 0; i < parameterKeyInfos.Count; ++i)
            {
                if (parameterKeyInfos.Items[i].Key == parameterKey)
                {
                    return parameterKeyInfos.Items[i].GetValueAccessor();
                }
            }

            LayoutCounter++;

            // Check layout if it exists
            if (layout != null)
            {
                foreach (var layoutParameterKeyInfo in layout.LayoutParameterKeyInfos)
                {
                    if (layoutParameterKeyInfo.Key == parameterKey)
                    {
                        parameterKeyInfos.Add(layoutParameterKeyInfo);
                        return layoutParameterKeyInfo.GetValueAccessor();
                    }
                }
            }

            // Compute size
            var elementSize = parameterKey.Size;
            var totalSize = elementSize;
            if (elementCount > 1)
                totalSize += (elementSize + 15) / 16 * 16 * (elementCount - 1);

            // Create offset entry
            var memberOffset = DataValues.Length;
            parameterKeyInfos.Add(new ParameterKeyInfo(parameterKey, memberOffset, elementCount));

            // We append at the end; resize array to accomodate new data
            Array.Resize(ref DataValues, DataValues.Length + totalSize);

            // Initialize default value
            if (parameterKey.DefaultValueMetadata != null)
            {
                fixed (byte* dataValues = DataValues)
                    parameterKey.DefaultValueMetadata.WriteBuffer((IntPtr)dataValues + memberOffset, 16);
            }

            return new Accessor(memberOffset, elementCount);
        }

        /// <summary>
        /// Gets pointer to directly copy blittable values.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public unsafe IntPtr GetValuePointer<T>(ValueParameter<T> parameter) where T : struct
        {
            fixed (byte* dataValues = DataValues)
                return (IntPtr)dataValues + parameter.Offset;
        }

        /// <summary>
        /// Sets an object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="value"></param>
        public void Set<T>(ObjectParameterKey<T> parameter, T value)
        {
            Set(GetAccessor(parameter), value);
        }

        /// <summary>
        /// Gets an object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public T Get<T>(ObjectParameterKey<T> parameter, bool createIfNew = false)
        {
            var accessor = GetAccessor(parameter, createIfNew);
            if (accessor.BindingSlot == -1)
                return parameter.DefaultValueMetadataT.DefaultValue;

            return Get(GetAccessor(parameter, createIfNew));
        }

        /// <summary>
        /// Sets a permutation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="value"></param>
        public void Set<T>(PermutationParameterKey<T> parameter, T value)
        {
            Set(GetAccessor(parameter), value);
        }

        /// <summary>
        /// Gets a permutation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public T Get<T>(PermutationParameterKey<T> parameter, bool createIfNew = false)
        {
            var accessor = GetAccessor(parameter, createIfNew);
            if (accessor.BindingSlot == -1)
                return parameter.DefaultValueMetadataT.DefaultValue;

            return Get(GetAccessor(parameter));
        }

        /// <summary>
        /// Sets a blittable value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="value"></param>
        public void Set<T>(ValueParameterKey<T> parameter, T value) where T : struct
        {
            Set(GetAccessor(parameter), ref value);
        }

        /// <summary>
        /// Sets a blittable value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="value"></param>
        public void Set<T>(ValueParameterKey<T> parameter, ref T value) where T : struct
        {
            Set(GetAccessor(parameter), ref value);
        }

        /// <summary>
        /// Sets blittable values.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="values"></param>
        public void Set<T>(ValueParameterKey<T> parameter, T[] values) where T : struct
        {
            Set(GetAccessor(parameter, values.Length), values.Length, ref values[0]);
        }

        /// <summary>
        /// Sets blittable values.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="values"></param>
        public void Set<T>(ValueParameterKey<T> parameter, int count, ref T firstValue) where T : struct
        {
            Set(GetAccessor(parameter, count), count, ref firstValue);
        }

        /// <summary>
        /// Gets a blittable value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public T Get<T>(ValueParameterKey<T> parameter) where T : struct
        {
            return Get(GetAccessor(parameter));
        }

        /// <summary>
        /// Gets blittable values.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public T[] GetValues<T>(ValueParameterKey<T> key) where T : struct
        {
            var parameter = GetAccessor(key);
            var data = GetValuePointer(parameter);

            // Align to float4
            var stride = (Utilities.SizeOf<T>() + 15) / 16 * 16;
            var values = new T[parameter.Count];
            for (int i = 0; i < values.Length; ++i)
            {
                Utilities.Read(data, ref values[i]);
                data += stride;
            }

            return values;
        }

        /// <summary>
        /// Sets a blittable value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="value"></param>
        public unsafe void Set<T>(ValueParameter<T> parameter, T value) where T : struct
        {
            fixed (byte* dataValues = DataValues)
                Utilities.Write((IntPtr)dataValues + parameter.Offset, ref value);
        }

        /// <summary>
        /// Sets a blittable value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="value"></param>
        public unsafe void Set<T>(ValueParameter<T> parameter, ref T value) where T : struct
        {
            fixed (byte* dataValues = DataValues)
                Utilities.Write((IntPtr)dataValues + parameter.Offset, ref value);
        }

        /// <summary>
        /// Sets blittable values.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="values"></param>
        public void Set<T>(ValueParameter<T> parameter, int count, ref T firstValue) where T : struct
        {
            var data = GetValuePointer(parameter);

            // Align to float4
            var stride = (Utilities.SizeOf<T>() + 15) / 16 * 16;
            var elementCount = parameter.Count;
            if (count > elementCount)
            {
                throw new IndexOutOfRangeException();
            }

            var value = Interop.Pin(ref firstValue);
            for (int i = 0; i < count; ++i)
            {
                Utilities.Write(data, ref value);
                data += stride;

                value = Interop.IncrementPinned(value);
            }
        }

        /// <summary>
        /// Sets a permutation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <param name="value"></param>
        public void Set<T>(PermutationParameter<T> parameter, T value)
        {
            if (!EqualityComparer<T>.Default.Equals((T)ObjectValues[parameter.BindingSlot], value))
                PermutationCounter++;

            ObjectValues[parameter.BindingSlot] = value;
        }

        /// <summary>
        /// Sets an object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameterAccessor"></param>
        /// <param name="value"></param>
        public void Set<T>(ObjectParameterAccessor<T> parameterAccessor, T value)
        {
            ObjectValues[parameterAccessor.BindingSlot] = value;
        }

        /// <summary>
        /// Gets a value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public unsafe T Get<T>(ValueParameter<T> parameter) where T : struct
        {
            fixed (byte* dataValues = DataValues)
                return Utilities.Read<T>((IntPtr)dataValues + parameter.Offset);
        }

        /// <summary>
        /// Gets a permutation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public T Get<T>(PermutationParameter<T> parameter)
        {
            return (T)ObjectValues[parameter.BindingSlot];
        }

        /// <summary>
        /// Gets an object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameterAccessor"></param>
        /// <returns></returns>
        public T Get<T>(ObjectParameterAccessor<T> parameterAccessor)
        {
            return (T)ObjectValues[parameterAccessor.BindingSlot];
        }

        public void SetObject(ParameterKey key, object value)
        {
            if (key.Type != ParameterKeyType.Permutation && key.Type != ParameterKeyType.Object)
                throw new InvalidOperationException("SetObject can only be used for Permutation or Object keys");

            var accessor = GetObjectParameterHelper(key);

            if (key.Type == ParameterKeyType.Permutation)
            {
                var oldValue = ObjectValues[accessor.Offset];
                if (oldValue != null && (value == null || !oldValue.Equals(value)) // oldValue non null => check equality
                    || (oldValue == null && value != null)) // oldValue null => check if value too
                        PermutationCounter++;
            }
            ObjectValues[accessor.Offset] = value;
        }

        public object GetObject(ParameterKey key)
        {
            if (key.Type != ParameterKeyType.Permutation && key.Type != ParameterKeyType.Object)
                throw new InvalidOperationException("SetObject can only be used for Permutation or Object keys");

            var accessor = GetObjectParameterHelper(key, false);
            if (accessor.Offset == -1)
                return null;

            return ObjectValues[accessor.Offset];
        }

        public bool Remove(ParameterKey key)
        {
            for (int i = 0; i < parameterKeyInfos.Count; ++i)
            {
                if (parameterKeyInfos[i].Key == key)
                {
                    parameterKeyInfos.SwapRemoveAt(i);
                    LayoutCounter++;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Clears the collection, including the layout.
        /// </summary>
        public void Clear()
        {
            DataValues = null;
            ObjectValues = null;
            layout = null;
            parameterKeyInfos.Clear();
        }

        /// <summary>
        /// Determines whether current collection contains a value for this key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool ContainsKey(ParameterKey key)
        {
            for (int i = 0; i < parameterKeyInfos.Count; ++i)
            {
                if (parameterKeyInfos[i].Key == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reorganizes internal data and resources to match the given objects, and append extra values at the end.
        /// </summary>
        /// <param name="layoutParameterKeyInfos"></param>
        /// <param name="resourceCount"></param>
        /// <param name="bufferSize"></param>
        /// <param name="constantBuffers"></param>
        /// <param name="descriptorSetLayouts"></param>
        public unsafe void UpdateLayout(ParameterCollectionLayout layout)
        {
            var oldLayout = this.layout;
            this.layout = layout;

            // Same layout, or removed layout
            if (oldLayout == layout || layout == null)
                return;

            var layoutParameterKeyInfos = layout.LayoutParameterKeyInfos;

            // Do a first pass to measure constant buffer size
            var newParameterKeyInfos = new FastList<ParameterKeyInfo>(Math.Max(1, parameterKeyInfos.Count));
            newParameterKeyInfos.AddRange(parameterKeyInfos);
            var processedParameters = new bool[parameterKeyInfos.Count];

            var bufferSize = layout.BufferSize;
            var resourceCount = layout.ResourceCount;

            foreach (var layoutParameterKeyInfo in layoutParameterKeyInfos)
            {
                // Find the same parameter in old collection
                // Is this parameter already added?
                for (int i = 0; i < parameterKeyInfos.Count; ++i)
                {
                    if (parameterKeyInfos[i].Key == layoutParameterKeyInfo.Key)
                    {
                        processedParameters[i] = true;
                        newParameterKeyInfos.Items[i] = layoutParameterKeyInfo;
                        break;
                    }
                }
            }

            // Append new elements that don't exist in new layouts (to preserve their values)
            for (int i = 0; i < processedParameters.Length; ++i)
            {
                // Skip parameters already processed before
                if (processedParameters[i])
                    continue;

                var parameterKeyInfo = newParameterKeyInfos[i];

                if (parameterKeyInfo.Offset != -1)
                {
                    // It's data
                    newParameterKeyInfos.Items[i].Offset = bufferSize;

                    var elementSize = newParameterKeyInfos.Items[i].Key.Size;
                    var elementCount = newParameterKeyInfos.Items[i].Count;
                    var totalSize = elementSize + (elementSize + 15) / 16 * 16 * (elementCount - 1);
                    bufferSize += totalSize;
                }
                else if (parameterKeyInfo.BindingSlot != -1)
                {
                    // It's a resource
                    newParameterKeyInfos.Items[i].BindingSlot = resourceCount++;
                }
            }
            
            var newDataValues = new byte[bufferSize];
            var newResourceValues = new object[resourceCount];

            // Update default values
            var bufferOffset = 0;
            foreach (var layoutParameterKeyInfo in layoutParameterKeyInfos)
            {
                if (layoutParameterKeyInfo.Offset != -1)
                {
                    // It's data
                    // TODO: Set default value
                    var defaultValueMetadata = layoutParameterKeyInfo.Key?.DefaultValueMetadata;
                    if (defaultValueMetadata != null)
                    {
                        fixed (byte* newDataValuesPtr = newDataValues)
                            defaultValueMetadata.WriteBuffer((IntPtr)newDataValuesPtr + bufferOffset + layoutParameterKeyInfo.Offset, 16);
                    }
                }
            }

            // Second pass to copy existing data at new offsets/slots
            for (int i = 0; i < parameterKeyInfos.Count; ++i)
            {
                var parameterKeyInfo = parameterKeyInfos[i];
                var newParameterKeyInfo = newParameterKeyInfos[i];

                if (newParameterKeyInfo.Offset != -1)
                {
                    var elementSize = newParameterKeyInfos.Items[i].Key.Size;
                    var newTotalSize = elementSize + (elementSize + 15) / 16 * 16 * (newParameterKeyInfo.Count - 1);
                    var totalSize = elementSize + (elementSize + 15) / 16 * 16 * (parameterKeyInfo.Count - 1);

                    // It's data
                    fixed (byte* dataValues = DataValues)
                    fixed (byte* newDataValuesPtr = newDataValues)
                        Utilities.CopyMemory((IntPtr)newDataValuesPtr + newParameterKeyInfo.Offset, (IntPtr)dataValues + parameterKeyInfo.Offset, Math.Min(newTotalSize, totalSize));
                }
                else if (newParameterKeyInfo.BindingSlot != -1)
                {
                    // It's a resource
                    newResourceValues[newParameterKeyInfo.BindingSlot] = ObjectValues[parameterKeyInfo.BindingSlot];
                }
            }

            // Update new content
            parameterKeyInfos = newParameterKeyInfos;

            DataValues = newDataValues;
            ObjectValues = newResourceValues;
        }

        protected Accessor GetObjectParameterHelper(ParameterKey parameterKey, bool createIfNew = true)
        {
            // Find existing first
            for (int i = 0; i < parameterKeyInfos.Count; ++i)
            {
                if (parameterKeyInfos.Items[i].Key == parameterKey)
                {
                    return parameterKeyInfos.Items[i].GetObjectAccessor();
                }
            }

            if (!createIfNew)
                return new Accessor(-1, 0);

            if (parameterKey.Type == ParameterKeyType.Permutation)
                PermutationCounter++;

            LayoutCounter++;

            // Check layout if it exists
            if (layout != null)
            {
                foreach (var layoutParameterKeyInfo in layout.LayoutParameterKeyInfos)
                {
                    if (layoutParameterKeyInfo.Key == parameterKey)
                    {
                        parameterKeyInfos.Add(layoutParameterKeyInfo);
                        return layoutParameterKeyInfo.GetObjectAccessor();
                    }
                }
            }

            // Create info entry
            var resourceValuesSize = ObjectValues?.Length ?? 0;
            Array.Resize(ref ObjectValues, resourceValuesSize + 1);
            parameterKeyInfos.Add(new ParameterKeyInfo(parameterKey, resourceValuesSize));

            // Initialize default value
            if (parameterKey.DefaultValueMetadata != null)
            {
                ObjectValues[resourceValuesSize] = parameterKey.DefaultValueMetadata.GetDefaultValue();
            }

            return new Accessor(resourceValuesSize, 1);
        }

        public class Serializer : ClassDataSerializer<ParameterCollection>
        {
            public override void Serialize(ref ParameterCollection parameterCollection, ArchiveMode mode, SerializationStream stream)
            {
                stream.Serialize(ref parameterCollection.parameterKeyInfos, mode);
                stream.SerializeExtended(ref parameterCollection.ObjectValues, mode);
                stream.Serialize(ref parameterCollection.DataValues, mode);
            }
        }

        public struct Copier
        {
            private CopyRange[] ranges;
            private ParameterCollection destination;
            private ParameterCollection source;
            private int sourceLayoutCounter;
            private string subKey;

            public Copier(ParameterCollection destination, ParameterCollection source, string subKey = null)
            {
                ranges = null;
                sourceLayoutCounter = 0;
                this.destination = destination;
                this.source = source;
                this.subKey = subKey;
            }

            public unsafe void Copy()
            {
                var destinationLayout = destination.Layout;

                // Note: we should provide a slow version for first copy during Extract (layout isn't known yet)
                if (destinationLayout == null)
                    throw new NotImplementedException();

                if (destinationLayout == source.Layout)
                {
                    // Easy, let's do a full copy!
                    PerformFastCopy(destinationLayout);
                }
                else
                {
                    if (ranges == null || sourceLayoutCounter != source.LayoutCounter)
                    {
                        Compile();

                        // Try again in case fast copy is possible
                        if (destinationLayout == source.Layout)
                        {
                            PerformFastCopy(destinationLayout);
                            return;
                        }
                    }

                    // Slower path: copy element by element
                    foreach (var range in ranges)
                    {
                        if (range.IsResource)
                        {
                            for (int i = 0; i < range.Size; ++i)
                            {
                                destination.ObjectValues[range.DestStart + i] = source.ObjectValues[range.SourceStart + i];
                            }
                        }
                        else if (range.IsData)
                        {
                            fixed (byte* destDataValues = destination.DataValues)
                            fixed (byte* sourceDataValues = source.DataValues)
                                Utilities.CopyMemory((IntPtr)destDataValues + range.DestStart, (IntPtr)sourceDataValues + range.SourceStart, range.Size);
                        }
                    }
                }
            }

            private unsafe void PerformFastCopy(ParameterCollectionLayout destinationLayout)
            {
                fixed (byte* destPtr = destination.DataValues)
                fixed (byte* sourcePtr = source.DataValues)
                    Utilities.CopyMemory((IntPtr)destPtr, (IntPtr)sourcePtr, destinationLayout.BufferSize);

                var resourceCount = destinationLayout.ResourceCount;
                for (int i = 0; i < resourceCount; ++i)
                    destination.ObjectValues[i] = source.ObjectValues[i];
            }

            private void Compile()
            {
                // If we are first, let's apply our layout!
                if (source.Layout == null && subKey == null)
                {
                    source.UpdateLayout(destination.Layout);
                    return;
                }
                else
                {
                    // TODO GRAPHICS REFACTOR optim: check if layout are the same
                    //if (source.Layout.LayoutParameterKeyInfos == destination.Layout.LayoutParameterKeyInfos)
                }

                var rangesList = new List<CopyRange>();

                // Try to match elements (both source and destination should have a layout by now)
                foreach (var parameterKeyInfo in destination.Layout.LayoutParameterKeyInfos)
                {
                    var sourceKey = parameterKeyInfo.Key;
                    if (subKey != null && sourceKey.Name.EndsWith(subKey))
                    {
                        // That's a match
                        var subkeyName = parameterKeyInfo.Key.Name.Substring(0, parameterKeyInfo.Key.Name.Length - subKey.Length);
                        sourceKey = ParameterKeys.FindByName(subkeyName);
                    }

                    if (parameterKeyInfo.Key.Type == ParameterKeyType.Value)
                    {
                        var sourceAccessor = source.GetValueAccessorHelper(sourceKey, parameterKeyInfo.Count);
                        var destAccessor = destination.GetValueAccessorHelper(parameterKeyInfo.Key, parameterKeyInfo.Count);
                        var elementCount = Math.Min(sourceAccessor.Count, destAccessor.Count);
                        var elementSize = parameterKeyInfo.Key.Size;
                        var size = (elementSize + 15) / 16 * 16 * (elementCount - 1) + elementSize;
                        rangesList.Add(new CopyRange { IsData = true, SourceStart = sourceAccessor.Offset, DestStart = destAccessor.Offset, Size = size });
                    }
                    else
                    {
                        var sourceAccessor = source.GetObjectParameterHelper(sourceKey);
                        var destAccessor = destination.GetObjectParameterHelper(parameterKeyInfo.Key);
                        var elementCount = Math.Min(sourceAccessor.Count, destAccessor.Count);
                        rangesList.Add(new CopyRange { IsResource = true, SourceStart = sourceAccessor.Offset, DestStart = destAccessor.Offset, Size = elementCount });
                    }
                }

                ranges = rangesList.ToArray();
                sourceLayoutCounter = source.LayoutCounter;
            }
        }

        public struct CompositionCopier
        {
            List<CopyRange> ranges;
            ParameterCollection destination;

            public bool IsValid => ranges != null;

            /// <summary>
            /// Copies data from source to destination according to previously compiled layout.
            /// </summary>
            /// <param name="source"></param>
            public unsafe void Copy(ParameterCollection source)
            {
                foreach (var range in ranges)
                {
                    if (range.IsResource)
                    {
                        for (int i = 0; i < range.Size; ++i)
                        {
                            destination.ObjectValues[range.DestStart + i] = source.ObjectValues[range.SourceStart + i];
                        }
                    }
                    else if (range.IsData)
                    {
                        fixed (byte* destDataValues = destination.DataValues)
                        fixed (byte* sourceDataValues = source.DataValues)
                        {
                            uint* destPtr = (uint*)(destDataValues + range.DestStart);
                            uint* sourcePtr = (uint*)(sourceDataValues + range.SourceStart);
                            var count = range.Size / 4;
                            for (int i = 0; i < count; ++i)
                                *destPtr++ = *sourcePtr++;
                        }
                    }
                }
            }

            /// <summary>
            /// Compute copy operations. Assumes destination layout is sequential.
            /// </summary>
            /// <param name="dest"></param>
            /// <param name="source"></param>
            /// <param name="keyRoot"></param>
            public void Compile(ParameterCollection dest, ParameterCollection source, string keyRoot)
            {
                ranges = new List<CopyRange>();
                destination = dest;
                var sourceLayout = new ParameterCollectionLayout();

                // Helper structures to try to keep range contiguous and have as few copy operations as possible (note: there can be some padding)
                var currentDataRange = new CopyRange { IsData = true, DestStart = -1 };
                var currentResourceRange = new CopyRange { IsResource = true, DestStart = -1 };

                // Iterate over each variable in dest, and if they match keyRoot, create the equivalent layout in source
                foreach (var parameterKeyInfo in dest.Layout.LayoutParameterKeyInfos)
                {
                    bool isResource = parameterKeyInfo.BindingSlot != -1;
                    bool isData = parameterKeyInfo.Offset != -1;

                    if (parameterKeyInfo.Key.Name.EndsWith(keyRoot))
                    {
                        // That's a match
                        var subkeyName = parameterKeyInfo.Key.Name.Substring(0, parameterKeyInfo.Key.Name.Length - keyRoot.Length);
                        var subkey = ParameterKeys.FindByName(subkeyName);

                        if (isData)
                        {
                            // First time since range reset, let's setup destination offset
                            if (currentDataRange.DestStart == -1)
                                currentDataRange.DestStart = parameterKeyInfo.Offset;

                            // Might be some empty space (padding)
                            currentDataRange.Size = parameterKeyInfo.Offset - currentDataRange.DestStart;

                            sourceLayout.LayoutParameterKeyInfos.Add(new ParameterKeyInfo(subkey, currentDataRange.SourceStart + currentDataRange.Size, parameterKeyInfo.Count));

                            var elementCount = parameterKeyInfo.Count;
                            var elementSize = parameterKeyInfo.Key.Size;
                            var size = (elementSize + 15) / 16 * 16 * (elementCount - 1) + elementSize;

                            currentDataRange.Size += size;
                        }
                        else if (isResource)
                        {
                            // First time since range reset, let's setup destination offset
                            if (currentResourceRange.DestStart == -1)
                                currentResourceRange.DestStart = parameterKeyInfo.BindingSlot;

                            // Might be some empty space (padding) (probably unlikely for resources...)
                            currentResourceRange.Size = parameterKeyInfo.BindingSlot - currentResourceRange.DestStart;

                            sourceLayout.LayoutParameterKeyInfos.Add(new ParameterKeyInfo(subkey, currentDataRange.SourceStart + currentDataRange.Size));

                            currentResourceRange.Size += parameterKeyInfo.Count;
                        }
                    }
                    else
                    {
                        // Found one item not part of the range, let's finish it
                        if (isData)
                            FlushRangeIfNecessary(ref currentDataRange);
                        else if (isResource)
                            FlushRangeIfNecessary(ref currentResourceRange);
                    }
                }

                // Finish ranges
                FlushRangeIfNecessary(ref currentDataRange);
                FlushRangeIfNecessary(ref currentResourceRange);

                // Update sizes
                sourceLayout.BufferSize = currentDataRange.SourceStart;
                sourceLayout.ResourceCount = currentResourceRange.SourceStart;

                source.UpdateLayout(sourceLayout);
            }

            private void FlushRangeIfNecessary(ref CopyRange currentRange)
            {
                if (currentRange.Size > 0)
                {
                    ranges.Add(currentRange);
                    currentRange.SourceStart += currentRange.Size;
                    currentRange.DestStart = -1;
                    currentRange.Size = 0;
                }
            }
        }

        struct CopyRange
        {
            public bool IsResource;
            public bool IsData;
            public int SourceStart;
            public int DestStart;
            public int Size;
        }

        public struct Accessor
        {
            public int Offset;
            public int Count;

            internal Accessor(int offset, int count)
            {
                Offset = offset;
                Count = count;
            }
        }
    }
}