﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Xaml;

namespace MS.Internal.Xaml.Context
{
    internal class XamlObjectWriterFactory: IXamlObjectWriterFactory
    {
        private XamlSavedContext _savedContext;
        private XamlObjectWriterSettings _parentSettings;

        public XamlObjectWriterFactory(ObjectWriterContext context)
        {
            _savedContext = context.GetSavedContext(SavedContextType.Template);
            _parentSettings = context.ServiceProvider_GetSettings();
        }

        #region IXamlObjectWriterFactory Members

        public XamlObjectWriter GetXamlObjectWriter(XamlObjectWriterSettings settings)
        {
            return new XamlObjectWriter(_savedContext, settings);
        }

        public XamlObjectWriterSettings GetParentSettings()
        {
            return new XamlObjectWriterSettings(_parentSettings);
        }

        #endregion

    }
}
