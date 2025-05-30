// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Reflection;
using System.Xaml;
using System.Xaml.Schema;
using MS.Internal.Xaml.Parser;

namespace MS.Internal.Xaml
{
    internal abstract class XamlContext
    {
        private XamlSchemaContext _schemaContext;
        private Func<string, string> _resolvePrefixCachedDelegate;
        protected Assembly _localAssembly;

        protected XamlContext(XamlSchemaContext schemaContext)
        {
            _schemaContext = schemaContext;
        }

        public XamlSchemaContext SchemaContext
        {
            get { return _schemaContext; }
        }

        public virtual Assembly LocalAssembly
        {
            get
            {
                return _localAssembly;
            }
            protected set
            {
                _localAssembly = value;
            }
        }

        // Only pass rootObjectType if the member is being looked up on the root object
        public XamlMember GetXamlProperty(XamlType xamlType, string propertyName, XamlType rootObjectType)
        {
            if (xamlType.IsUnknown)
            {
                return null;
            }

            XamlMember member = xamlType.GetMember(propertyName);
            return IsVisible(member, rootObjectType) ? member : null;
        }

        public XamlMember GetXamlAttachableProperty(XamlType xamlType, string propertyName)
        {
            if (xamlType.IsUnknown)
            {
                return null;
            }

            XamlMember member = xamlType.GetAttachableMember(propertyName);
            return IsVisible(member, null) ? member : null;
        }

        /// <summary>
        /// Resolves a property of the form 'Foo.Bar' or 'a:Foo.Bar', in
        /// in the context of a parent tag.  The parent tagType may or may not
        /// be covariant with the ownerType.  In the case of dotted attribute
        /// syntax, the namespace my be passed in.
        /// </summary>
        /// <param name="tagType">The xamlType of the enclosing Tag</param>
        /// <param name="tagNamespace">The namespace of the enclosing Tag</param>
        /// <param name="propName">The dotted name of the property</param>
        /// <param name="tagIsRoot">Whether the tag is the root of the document</param>
        /// <returns></returns>
        public XamlMember GetDottedProperty(XamlType tagType, string tagNamespace, XamlPropertyName propName, bool tagIsRoot)
        {
            if (tagType is null)
            {
                throw new XamlInternalException(SR.Format(SR.ParentlessPropertyElement, propName.ScopedName));
            }

            XamlMember property = null;
            XamlType ownerType = null;
            string ns = ResolveXamlNameNS(propName);
            if (ns is null)
            {
                throw new XamlParseException(SR.Format(SR.PrefixNotFound, propName.Prefix));
            }

            XamlType rootTagType = tagIsRoot ? tagType : null;

            // If we have <foo x:TA="" foo.bar=""/> we want foo in foo.bar to match the tag
            // type since there is no way to specify generic syntax in dotted property notation
            // If that fails, then we fall back to the non-generic case below.
            bool ownerTypeMatchesGenericTagType = false;
            if (tagType.IsGeneric)
            {
                ownerTypeMatchesGenericTagType = PropertyTypeMatchesGenericTagType(tagType, tagNamespace, ns, propName.OwnerName);
                if (ownerTypeMatchesGenericTagType)
                {
                    property = GetInstanceOrAttachableProperty(tagType, propName.Name, rootTagType);
                    if (property is not null)
                    {
                        return property;
                    }
                }
            }

            // Non-generic case, just resolve using the namespace and name
            XamlTypeName ownerTypeName = new XamlTypeName(ns, propName.Owner.Name);
            ownerType = GetXamlType(ownerTypeName, true);
            bool canAssignTagTypeToOwnerType = tagType.CanAssignTo(ownerType);

            if (canAssignTagTypeToOwnerType)
            {
                property = GetInstanceOrAttachableProperty(ownerType, propName.Name, rootTagType);
            }
            else
            {
                property = GetXamlAttachableProperty(ownerType, propName.Name);
            }

            if (property is null)
            {
                // This is an unknown property.
                // We don't know for sure whether or not it's attachable, so we go with our best guess.
                // If the owner type is same as the generic tag type, or is assignable to the tag type,
                // it's probably not attachable.
                XamlType declaringType = ownerTypeMatchesGenericTagType ? tagType : ownerType;
                if (ownerTypeMatchesGenericTagType || canAssignTagTypeToOwnerType)
                {
                    property = CreateUnknownMember(declaringType, propName.Name);
                }
                else
                {
                    property = CreateUnknownAttachableMember(declaringType, propName.Name);
                }
            }

            return property;
        }

        public string GetAttributeNamespace(XamlPropertyName propName, string tagNamespace)
        {
            string ns = null;

            // Get the proper XamlNamespace for the Property
            // If prefix is "" then
            // Normal Properties resolve to the ownerType namespace
            // Attachable properties resolve to the actual "" namespace
            if (string.IsNullOrEmpty(propName.Prefix) && !propName.IsDotted)
            {
                ns = tagNamespace;
            }
            else
            {
                ns = ResolveXamlNameNS(propName);
            }

            return ns;
        }

        public XamlMember GetNoDotAttributeProperty(XamlType tagType, XamlPropertyName propName,
                                                    string tagNamespace, string propUsageNamespace, bool tagIsRoot)
        {
            XamlMember property = null;
            // workaround: tagNamespace will always be null coming from MeScanner.
            // Second line of if just handles tagNamespace always being null from MEScanner
            // Correct fix is to fix MEScanner and remove second line
            if ((propUsageNamespace == tagNamespace)
                || (tagNamespace is null && propUsageNamespace is not null && tagType.GetXamlNamespaces().Contains(propUsageNamespace)))
            {
                XamlType rootTagType = tagIsRoot ? tagType : null;
                property = GetXamlProperty(tagType, propName.Name, rootTagType);

                // Sometimes Attached properties look like normal properties.
                // [Attribute case] The above lookup fails and fall into here.
                // <Grid> <Grid Row="0"/> </Grid>
                if (property is null)
                {
                    property = GetXamlAttachableProperty(tagType, propName.Name);
                }
            }

            // Not Simple, not Attachable, look for Directives.
            if (property is null && propUsageNamespace is not null)
            {
                // A processing attribute like;  x:Key  x:Name
                XamlDirective directive = SchemaContext.GetXamlDirective(propUsageNamespace, propName.Name);
                if (directive is not null)
                {
                    if ((directive.AllowedLocation & AllowedMemberLocations.Attribute) == AllowedMemberLocations.None)
                    {
                        // Need a way to surface up this usage error now that
                        // we don't have UnknownProperty.Exception
                        directive = new XamlDirective(propUsageNamespace, propName.Name);
                    }

                    property = directive;
                }
            }

            if (property is null)
            {
                if (tagNamespace == propUsageNamespace)
                {
                    // Unknown simple property
                    property = new XamlMember(propName.Name, tagType, false);
                }
                else
                {
                    // Unknown directive
                    property = new XamlDirective(propUsageNamespace, propName.Name);
                }
            }

            return property;
        }

        public abstract void AddNamespacePrefix(string prefix, string xamlNamespace);
        public abstract string FindNamespaceByPrefix(string prefix);
        public abstract IEnumerable<NamespaceDeclaration> GetNamespacePrefixes();

        // -------------------- internal ------------------------

        private XamlType GetXamlTypeOrUnknown(XamlTypeName typeName)
        {
            return GetXamlType(typeName, true);
        }

        internal XamlType GetXamlType(XamlName typeName)
        {
            return GetXamlType(typeName, false);
        }

        internal XamlType GetXamlType(XamlName typeName, bool returnUnknownTypesOnFailure)
        {
            XamlTypeName fullTypeName = GetXamlTypeName(typeName);
            return GetXamlType(fullTypeName, returnUnknownTypesOnFailure);
        }

        internal XamlTypeName GetXamlTypeName(XamlName typeName)
        {
            string xamlNs = ResolveXamlNameNS(typeName);
            if (xamlNs is null)
            {
                throw new XamlParseException(SR.Format(SR.PrefixNotFound, typeName.Prefix));
            }

            return new XamlTypeName(xamlNs, typeName.Name);
        }

        internal XamlType GetXamlType(XamlTypeName typeName)
        {
            return GetXamlType(typeName, false, false);
        }

        internal XamlType GetXamlType(XamlTypeName typeName, bool returnUnknownTypesOnFailure)
        {
            return GetXamlType(typeName, returnUnknownTypesOnFailure, false);
        }

        internal XamlType GetXamlType(XamlTypeName typeName, bool returnUnknownTypesOnFailure,
            bool skipVisibilityCheck)
        {
            Debug.Assert(typeName is not null, "typeName cannot be null and should have been checked before now");
            Debug.Assert(typeName.Name is not null, "typeName.Name cannot be null and should have been checked before now");
            Debug.Assert(typeName.Namespace is not null);
            XamlType xamlType = _schemaContext.GetXamlType(typeName);
            if (xamlType is not null && !skipVisibilityCheck && !xamlType.IsVisibleTo(LocalAssembly))
            {
                xamlType = null;
            }

            if (xamlType is null && returnUnknownTypesOnFailure)
            {
                XamlType[] typeArgs = null;
                if (typeName.HasTypeArgs)
                {
                    List<XamlTypeName> typeNames = typeName.TypeArgumentsList;
                    typeArgs = new XamlType[typeNames.Count];
                    for (int i = 0; i < typeArgs.Length; i++)
                    {
                        typeArgs[i] = GetXamlTypeOrUnknown(typeNames[i]);
                    }
                }

                xamlType = new XamlType(typeName.Namespace, typeName.Name, typeArgs, SchemaContext);
            }

            return xamlType;
        }

        internal Func<string, string> ResolvePrefixCachedDelegate
        {
            get
            {
                if (_resolvePrefixCachedDelegate is null)
                {
                    _resolvePrefixCachedDelegate = new Func<string, string>(FindNamespaceByPrefix);
                }

                return _resolvePrefixCachedDelegate;
            }
        }

        private string ResolveXamlNameNS(XamlName name)
        {
            return name.Namespace ?? FindNamespaceByPrefix(name.Prefix);
        }

        internal XamlType ResolveXamlType(string qName, bool skipVisibilityCheck)
        {
            string error;
            XamlTypeName typeName = XamlTypeName.ParseInternal(qName, ResolvePrefixCachedDelegate, out error);
            if (typeName is null)
            {
                throw new XamlParseException(error);
            }

            return GetXamlType(typeName, false, skipVisibilityCheck);
        }

        internal XamlMember ResolveDirectiveProperty(string xamlNS, string name)
        {
            if (xamlNS is not null)
            {
                return SchemaContext.GetXamlDirective(xamlNS, name);
            }

            return null;
        }

        // Only pass rootObjectType if the member is being looked up on the root object
        internal virtual bool IsVisible(XamlMember member, XamlType rootObjectType)
        {
            return true;
        }

        private XamlMember CreateUnknownMember(XamlType declaringType, string name)
        {
            return new XamlMember(name, declaringType, false);
        }

        private XamlMember CreateUnknownAttachableMember(XamlType declaringType, string name)
        {
            return new XamlMember(name, declaringType, true);
        }

        private bool PropertyTypeMatchesGenericTagType(XamlType tagType, string tagNs, string propNs, string propTypeName)
        {
            // Schema can potentially remap names and namespaces from what is requested in GetXamlType.
            // However, a failed GetXamlType call is expensive, we don't want to do one unnecessarily.
            // So we try to match the property type to the generic type if:
            // - The xml namespaces are an exact match
            // - The type names are an exact match
            // - The property is in any of the same namespaces as the tag type
            if (tagNs != propNs && tagType.Name != propTypeName &&
                !tagType.GetXamlNamespaces().Contains(propNs))
            {
                return false;
            }

            XamlType propertyType = GetXamlType(propNs, propTypeName, tagType.TypeArguments);
            return tagType == propertyType;
        }

        private XamlMember GetInstanceOrAttachableProperty(XamlType tagType, string propName, XamlType rootTagType)
        {
            XamlMember property = GetXamlProperty(tagType, propName, rootTagType);
            if (property is null)
            {
                // Sometimes Attached properties look like normal properties.
                // The above lookup fails and fall into here.
                // ie: <Grid> <Grid> <Grid.Row>0<Grid.Row/> </Grid> </Grid>
                // or: <Grid> <Grid Grid.Row="0" /> </Grid>
                property = GetXamlAttachableProperty(tagType, propName);
            }

            return property;
        }

        private XamlType GetXamlType(string ns, string name, IList<XamlType> typeArguments)
        {
            XamlType[] typeArgArray = new XamlType[typeArguments.Count];
            typeArguments.CopyTo(typeArgArray, 0);
            XamlType xamlType = _schemaContext.GetXamlType(ns, name, typeArgArray);
            if (xamlType is not null && !xamlType.IsVisibleTo(LocalAssembly))
            {
                xamlType = null;
            }

            return xamlType;
        }
    }
}
