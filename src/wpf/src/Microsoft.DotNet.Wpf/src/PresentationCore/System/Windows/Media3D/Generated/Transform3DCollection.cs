// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//
// This file was generated, please do not edit it directly.
//
// Please see MilCodeGen.html for more information.
//

using MS.Internal;
using MS.Internal.Collections;
using MS.Utility;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Markup;
using System.Windows.Media.Media3D.Converters;
using System.Windows.Media.Animation;
using System.Windows.Media.Composition;

namespace System.Windows.Media.Media3D
{
    /// <summary>
    /// A collection of Transform3D objects.
    /// </summary>

    public sealed partial class Transform3DCollection : Animatable, IList, IList<Transform3D>
    {
        //------------------------------------------------------
        //
        //  Public Methods
        //
        //------------------------------------------------------

        #region Public Methods

        /// <summary>
        ///     Shadows inherited Clone() with a strongly typed
        ///     version for convenience.
        /// </summary>
        public new Transform3DCollection Clone()
        {
            return (Transform3DCollection)base.Clone();
        }

        /// <summary>
        ///     Shadows inherited CloneCurrentValue() with a strongly typed
        ///     version for convenience.
        /// </summary>
        public new Transform3DCollection CloneCurrentValue()
        {
            return (Transform3DCollection)base.CloneCurrentValue();
        }




        #endregion Public Methods

        //------------------------------------------------------
        //
        //  Public Properties
        //
        //------------------------------------------------------


        #region IList<T>

        /// <summary>
        ///     Adds "value" to the list
        /// </summary>
        public void Add(Transform3D value)
        {
            AddHelper(value);
        }

        /// <summary>
        ///     Removes all elements from the list
        /// </summary>
        public void Clear()
        {
            WritePreamble();

            // As part of Clear()'ing the collection, we will iterate it and call
            // OnFreezablePropertyChanged and OnRemove for each item.
            // However, OnRemove assumes that the item to be removed has already been
            // pulled from the underlying collection.  To statisfy this condition,
            // we store the old collection and clear _collection before we call these methods.
            // As Clear() semantics do not include TrimToFit behavior, we create the new
            // collection storage at the same size as the previous.  This is to provide
            // as close as possible the same perf characteristics as less complicated collections.
            FrugalStructList<Transform3D> oldCollection = _collection;
            _collection = new FrugalStructList<Transform3D>(_collection.Capacity);

            for (int i = oldCollection.Count - 1; i >= 0; i--)
            {
                OnFreezablePropertyChanged(/* oldValue = */ oldCollection[i], /* newValue = */ null);

                // Fire the OnRemove handlers for each item.  We're not ensuring that
                // all OnRemove's get called if a resumable exception is thrown.
                // At this time, these call-outs are not public, so we do not handle exceptions.
                OnRemove( /* oldValue */ oldCollection[i]);
            }

            ++_version;
            WritePostscript();
        }

        /// <summary>
        ///     Determines if the list contains "value"
        /// </summary>
        public bool Contains(Transform3D value)
        {
            ReadPreamble();

            return _collection.Contains(value);
        }

        /// <summary>
        ///     Returns the index of "value" in the list
        /// </summary>
        public int IndexOf(Transform3D value)
        {
            ReadPreamble();

            return _collection.IndexOf(value);
        }

        /// <summary>
        ///     Inserts "value" into the list at the specified position
        /// </summary>
        public void Insert(int index, Transform3D value)
        {
            if (value == null)
            {
                throw new System.ArgumentException(SR.Collection_NoNull);
            }

            WritePreamble();

            OnFreezablePropertyChanged(/* oldValue = */ null, /* newValue = */ value);

            _collection.Insert(index, value);
            OnInsert(value);


            ++_version;
            WritePostscript();
        }

        /// <summary>
        ///     Removes "value" from the list
        /// </summary>
        public bool Remove(Transform3D value)
        {
            WritePreamble();

            // By design collections "succeed silently" if you attempt to remove an item
            // not in the collection.  Therefore we need to first verify the old value exists
            // before calling OnFreezablePropertyChanged.  Since we already need to locate
            // the item in the collection we keep the index and use RemoveAt(...) to do
            // the work.  (Windows OS #1016178)

            // We use the public IndexOf to guard our UIContext since OnFreezablePropertyChanged
            // is only called conditionally.  IList.IndexOf returns -1 if the value is not found.
            int index = IndexOf(value);

            if (index >= 0)
            {
                Transform3D oldValue = _collection[index];

                OnFreezablePropertyChanged(oldValue, null);

                _collection.RemoveAt(index);

                OnRemove(oldValue);


                ++_version;
                WritePostscript();

                return true;
            }

            // Collection_Remove returns true, calls WritePostscript,
            // increments version, and does UpdateResource if it succeeds

            return false;
        }

        /// <summary>
        ///     Removes the element at the specified index
        /// </summary>
        public void RemoveAt(int index)
        {
            RemoveAtWithoutFiringPublicEvents(index);

            // RemoveAtWithoutFiringPublicEvents incremented the version

            WritePostscript();
        }


        /// <summary>
        ///     Removes the element at the specified index without firing
        ///     the public Changed event.
        ///     The caller - typically a public method - is responsible for calling
        ///     WritePostscript if appropriate.
        /// </summary>
        internal void RemoveAtWithoutFiringPublicEvents(int index)
        {
            WritePreamble();

            Transform3D oldValue = _collection[ index ];

            OnFreezablePropertyChanged(oldValue, null);

            _collection.RemoveAt(index);

            OnRemove(oldValue);


            ++_version;

            // No WritePostScript to avoid firing the Changed event.
        }


        /// <summary>
        ///     Indexer for the collection
        /// </summary>
        public Transform3D this[int index]
        {
            get
            {
                ReadPreamble();

                return _collection[index];
            }
            set
            {
                if (value == null)
                {
                    throw new System.ArgumentException(SR.Collection_NoNull);
                }

                WritePreamble();

                if (!Object.ReferenceEquals(_collection[ index ], value))
                {

                    Transform3D oldValue = _collection[ index ];
                    OnFreezablePropertyChanged(oldValue, value);

                    _collection[ index ] = value;

                    OnSet(oldValue, value);
                }


                ++_version;
                WritePostscript();
            }
        }

        #endregion

        #region ICollection<T>

        /// <summary>
        ///     The number of elements contained in the collection.
        /// </summary>
        public int Count
        {
            get
            {
                ReadPreamble();

                return _collection.Count;
            }
        }

        /// <summary>
        ///     Copies the elements of the collection into "array" starting at "index"
        /// </summary>
        public void CopyTo(Transform3D[] array, int index)
        {
            ReadPreamble();

            ArgumentNullException.ThrowIfNull(array);

            // This will not throw in the case that we are copying
            // from an empty collection.  This is consistent with the
            // BCL Collection implementations. (Windows 1587365)
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, array.Length - _collection.Count);

            _collection.CopyTo(array, index);
        }

        bool ICollection<Transform3D>.IsReadOnly
        {
            get
            {
                ReadPreamble();

                return IsFrozen;
            }
        }

        #endregion

        #region IEnumerable<T>

        /// <summary>
        /// Returns an enumerator for the collection
        /// </summary>
        public Enumerator GetEnumerator()
        {
            ReadPreamble();

            return new Enumerator(this);
        }

        IEnumerator<Transform3D> IEnumerable<Transform3D>.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion

        #region IList

        bool IList.IsReadOnly
        {
            get
            {
                return ((ICollection<Transform3D>)this).IsReadOnly;
            }
        }

        bool IList.IsFixedSize
        {
            get
            {
                ReadPreamble();

                return IsFrozen;
            }
        }

        object IList.this[int index]
        {
            get
            {
                return this[index];
            }
            set
            {
                // Forwards to typed implementation
                this[index] = Cast(value);
            }
        }

        int IList.Add(object value)
        {
            // Forward to typed helper
            return AddHelper(Cast(value));
        }

        bool IList.Contains(object value)
        {
            return Contains(value as Transform3D);
        }

        int IList.IndexOf(object value)
        {
            return IndexOf(value as Transform3D);
        }

        void IList.Insert(int index, object value)
        {
            // Forward to IList<T> Insert
            Insert(index, Cast(value));
        }

        void IList.Remove(object value)
        {
            Remove(value as Transform3D);
        }

        #endregion

        #region ICollection

        void ICollection.CopyTo(Array array, int index)
        {
            ReadPreamble();

            ArgumentNullException.ThrowIfNull(array);

            // This will not throw in the case that we are copying
            // from an empty collection.  This is consistent with the
            // BCL Collection implementations. (Windows 1587365)
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, array.Length - _collection.Count);

            if (array.Rank != 1)
            {
                throw new ArgumentException(SR.Collection_BadRank);
            }

            // Elsewhere in the collection we throw an AE when the type is
            // bad so we do it here as well to be consistent
            try
            {
                int count = _collection.Count;
                for (int i = 0; i < count; i++)
                {
                    array.SetValue(_collection[i], index + i);
                }
            }
            catch (InvalidCastException e)
            {
                throw new ArgumentException(SR.Format(SR.Collection_BadDestArray, this.GetType().Name), e);
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                ReadPreamble();

                return IsFrozen || Dispatcher != null;
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                ReadPreamble();
                return this;
            }
        }
        #endregion

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion

        #region Internal Helpers

        /// <summary>
        /// A frozen empty Transform3DCollection.
        /// </summary>
        internal static Transform3DCollection Empty
        {
            get
            {
                if (s_empty == null)
                {
                    Transform3DCollection collection = new Transform3DCollection();
                    collection.Freeze();
                    s_empty = collection;
                }

                return s_empty;
            }
        }

        /// <summary>
        /// Helper to return read only access.
        /// </summary>
        internal Transform3D Internal_GetItem(int i)
        {
            return _collection[i];
        }

        /// <summary>
        ///     Freezable collections need to notify their contained Freezables
        ///     about the change in the InheritanceContext
        /// </summary>
        internal override void OnInheritanceContextChangedCore(EventArgs args)
        {
            base.OnInheritanceContextChangedCore(args);

            for (int i = 0; i < this.Count; i++)
            {
                DependencyObject inheritanceChild = _collection[i];
                if (inheritanceChild != null && inheritanceChild.InheritanceContext == this)
                {
                    inheritanceChild.OnInheritanceContextChanged(args);
                }
            }
        }

        #endregion

        #region Private Helpers

        private Transform3D Cast(object value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!(value is Transform3D))
            {
                throw new System.ArgumentException(SR.Format(SR.Collection_BadType, this.GetType().Name, value.GetType().Name, "Transform3D"));
            }

            return (Transform3D) value;
        }

        // IList.Add returns int and IList<T>.Add does not. This
        // is called by both Adds and IList<T>'s just ignores the
        // integer
        private int AddHelper(Transform3D value)
        {
            int index = AddWithoutFiringPublicEvents(value);

            // AddAtWithoutFiringPublicEvents incremented the version

            WritePostscript();

            return index;
        }

        internal int AddWithoutFiringPublicEvents(Transform3D value)
        {
            int index = -1;

            if (value == null)
            {
                throw new System.ArgumentException(SR.Collection_NoNull);
            }
            WritePreamble();
            Transform3D newValue = value;
            OnFreezablePropertyChanged(/* oldValue = */ null, newValue);
            index = _collection.Add(newValue);
            OnInsert(newValue);


            ++_version;

            // No WritePostScript to avoid firing the Changed event.

            return index;
        }

        internal event ItemInsertedHandler ItemInserted;
        internal event ItemRemovedHandler ItemRemoved;

        private void OnInsert(object item)
        {
            if (ItemInserted != null)
            {
                ItemInserted(this, item);
            }
        }

        private void OnRemove(object oldValue)
        {
            if (ItemRemoved != null)
            {
                ItemRemoved(this, oldValue);
            }
        }

        private void OnSet(object oldValue, object newValue)
        {
            OnInsert(newValue);
            OnRemove(oldValue);
        }

        #endregion Private Helpers

        private static Transform3DCollection s_empty;


        #region Public Properties



        #endregion Public Properties

        //------------------------------------------------------
        //
        //  Protected Methods
        //
        //------------------------------------------------------

        #region Protected Methods

        /// <summary>
        /// Implementation of <see cref="System.Windows.Freezable.CreateInstanceCore">Freezable.CreateInstanceCore</see>.
        /// </summary>
        /// <returns>The new Freezable.</returns>
        protected override Freezable CreateInstanceCore()
        {
            return new Transform3DCollection();
        }
        /// <summary>
        /// Implementation of Freezable.CloneCore()
        /// </summary>
        protected override void CloneCore(Freezable source)
        {
            Transform3DCollection sourceTransform3DCollection = (Transform3DCollection)source;

            base.CloneCore(source);

            int count = sourceTransform3DCollection._collection.Count;

            _collection = new FrugalStructList<Transform3D>(count);

            for (int i = 0; i < count; i++)
            {
                Transform3D newValue = (Transform3D)sourceTransform3DCollection._collection[i].Clone();
                OnFreezablePropertyChanged(/* oldValue = */ null, newValue);
                _collection.Add(newValue);
                OnInsert(newValue);
            }

        }
        /// <summary>
        /// Implementation of Freezable.CloneCurrentValueCore()
        /// </summary>
        protected override void CloneCurrentValueCore(Freezable source)
        {
            Transform3DCollection sourceTransform3DCollection = (Transform3DCollection)source;

            base.CloneCurrentValueCore(source);

            int count = sourceTransform3DCollection._collection.Count;

            _collection = new FrugalStructList<Transform3D>(count);

            for (int i = 0; i < count; i++)
            {
                Transform3D newValue = (Transform3D)sourceTransform3DCollection._collection[i].CloneCurrentValue();
                OnFreezablePropertyChanged(/* oldValue = */ null, newValue);
                _collection.Add(newValue);
                OnInsert(newValue);
            }

        }
        /// <summary>
        /// Implementation of Freezable.GetAsFrozenCore()
        /// </summary>
        protected override void GetAsFrozenCore(Freezable source)
        {
            Transform3DCollection sourceTransform3DCollection = (Transform3DCollection)source;

            base.GetAsFrozenCore(source);

            int count = sourceTransform3DCollection._collection.Count;

            _collection = new FrugalStructList<Transform3D>(count);

            for (int i = 0; i < count; i++)
            {
                Transform3D newValue = (Transform3D)sourceTransform3DCollection._collection[i].GetAsFrozen();
                OnFreezablePropertyChanged(/* oldValue = */ null, newValue);
                _collection.Add(newValue);
                OnInsert(newValue);
            }

        }
        /// <summary>
        /// Implementation of Freezable.GetCurrentValueAsFrozenCore()
        /// </summary>
        protected override void GetCurrentValueAsFrozenCore(Freezable source)
        {
            Transform3DCollection sourceTransform3DCollection = (Transform3DCollection)source;

            base.GetCurrentValueAsFrozenCore(source);

            int count = sourceTransform3DCollection._collection.Count;

            _collection = new FrugalStructList<Transform3D>(count);

            for (int i = 0; i < count; i++)
            {
                Transform3D newValue = (Transform3D)sourceTransform3DCollection._collection[i].GetCurrentValueAsFrozen();
                OnFreezablePropertyChanged(/* oldValue = */ null, newValue);
                _collection.Add(newValue);
                OnInsert(newValue);
            }

        }
        /// <summary>
        /// Implementation of <see cref="System.Windows.Freezable.FreezeCore">Freezable.FreezeCore</see>.
        /// </summary>
        protected override bool FreezeCore(bool isChecking)
        {
            bool canFreeze = base.FreezeCore(isChecking);

            int count = _collection.Count;
            for (int i = 0; i < count && canFreeze; i++)
            {
                canFreeze &= Freezable.Freeze(_collection[i], isChecking);
            }

            return canFreeze;
        }

        #endregion ProtectedMethods

        //------------------------------------------------------
        //
        //  Internal Methods
        //
        //------------------------------------------------------

        #region Internal Methods









        #endregion Internal Methods

        //------------------------------------------------------
        //
        //  Internal Properties
        //
        //------------------------------------------------------

        #region Internal Properties





        #endregion Internal Properties

        //------------------------------------------------------
        //
        //  Dependency Properties
        //
        //------------------------------------------------------

        #region Dependency Properties



        #endregion Dependency Properties

        //------------------------------------------------------
        //
        //  Internal Fields
        //
        //------------------------------------------------------

        #region Internal Fields




        internal FrugalStructList<Transform3D> _collection;
        internal uint _version = 0;


        #endregion Internal Fields

        #region Enumerator
        /// <summary>
        /// Enumerates the items in a Transform3DCollection
        /// </summary>
        public struct Enumerator : IEnumerator, IEnumerator<Transform3D>
        {
            #region Constructor

            internal Enumerator(Transform3DCollection list)
            {
                Debug.Assert(list != null, "list may not be null.");

                _list = list;
                _version = list._version;
                _index = -1;
                _current = default(Transform3D);
            }

            #endregion

            #region Methods

            void IDisposable.Dispose()
            {

            }

            /// <summary>
            /// Advances the enumerator to the next element of the collection.
            /// </summary>
            /// <returns>
            /// true if the enumerator was successfully advanced to the next element,
            /// false if the enumerator has passed the end of the collection.
            /// </returns>
            public bool MoveNext()
            {
                _list.ReadPreamble();

                if (_version == _list._version)
                {
                    if (_index > -2 && _index < _list._collection.Count - 1)
                    {
                        _current = _list._collection[++_index];
                        return true;
                    }
                    else
                    {
                        _index = -2; // -2 indicates "past the end"
                        return false;
                    }
                }
                else
                {
                    throw new InvalidOperationException(SR.Enumerator_CollectionChanged);
                }
            }

            /// <summary>
            /// Sets the enumerator to its initial position, which is before the
            /// first element in the collection.
            /// </summary>
            public void Reset()
            {
                _list.ReadPreamble();

                if (_version == _list._version)
                {
                    _index = -1;
                }
                else
                {
                    throw new InvalidOperationException(SR.Enumerator_CollectionChanged);
                }
            }

            #endregion

            #region Properties

            object IEnumerator.Current
            {
                get
                {
                    return this.Current;
                }
            }

            /// <summary>
            /// Current element
            ///
            /// The behavior of IEnumerable&lt;T>.Current is undefined
            /// before the first MoveNext and after we have walked
            /// off the end of the list. However, the IEnumerable.Current
            /// contract requires that we throw exceptions
            /// </summary>
            public Transform3D Current
            {
                get
                {
                    if (_index > -1)
                    {
                        return _current;
                    }
                    else if (_index == -1)
                    {
                        throw new InvalidOperationException(SR.Enumerator_NotStarted);
                    }
                    else
                    {
                        Debug.Assert(_index == -2, "expected -2, got " + _index + "\n");
                        throw new InvalidOperationException(SR.Enumerator_ReachedEnd);
                    }
                }
            }

            #endregion

            #region Data
            private Transform3D _current;
            private Transform3DCollection _list;
            private uint _version;
            private int _index;
            #endregion
        }
        #endregion

        #region Constructors

        //------------------------------------------------------
        //
        //  Constructors
        //
        //------------------------------------------------------


        /// <summary>
        /// Initializes a new instance that is empty.
        /// </summary>
        public Transform3DCollection()
        {
            _collection = new FrugalStructList<Transform3D>();
        }

        /// <summary>
        /// Initializes a new instance that is empty and has the specified initial capacity.
        /// </summary>
        /// <param name="capacity"> int - The number of elements that the new list is initially capable of storing. </param>
        public Transform3DCollection(int capacity)
        {
            _collection = new FrugalStructList<Transform3D>(capacity);
        }

        /// <summary>
        /// Creates a Transform3DCollection with all of the same elements as collection
        /// </summary>
        public Transform3DCollection(IEnumerable<Transform3D> collection)
        {
            // The WritePreamble and WritePostscript aren't technically necessary
            // in the constructor as of 1/20/05 but they are put here in case
            // their behavior changes at a later date

            WritePreamble();

            ArgumentNullException.ThrowIfNull(collection);

            bool needsItemValidation = true;
            ICollection<Transform3D> icollectionOfT = collection as ICollection<Transform3D>;

            if (icollectionOfT != null)
            {
                _collection = new FrugalStructList<Transform3D>(icollectionOfT);
            }
            else
            {
                ICollection icollection = collection as ICollection;

                if (icollection != null) // an IC but not and IC<T>
                {
                    _collection = new FrugalStructList<Transform3D>(icollection);
                }
                else // not a IC or IC<T> so fall back to the slower Add
                {
                    _collection = new FrugalStructList<Transform3D>();

                    foreach (Transform3D item in collection)
                    {
                        if (item == null)
                        {
                            throw new System.ArgumentException(SR.Collection_NoNull);
                        }
                        Transform3D newValue = item;
                        OnFreezablePropertyChanged(/* oldValue = */ null, newValue);
                        _collection.Add(newValue);
                        OnInsert(newValue);
                    }

                    needsItemValidation = false;
                }
            }

            if (needsItemValidation)
            {
                foreach (Transform3D item in collection)
                {
                    if (item == null)
                    {
                        throw new System.ArgumentException(SR.Collection_NoNull);
                    }
                    OnFreezablePropertyChanged(/* oldValue = */ null, item);
                    OnInsert(item);
                }
            }


            WritePostscript();
        }

        #endregion Constructors
    }
}
