﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Reflection;
using System.Xaml.MS.Impl;

namespace System.Xaml.Schema
{
    public class XamlMemberInvoker
    {
        private static XamlMemberInvoker s_Directive;
        private static XamlMemberInvoker s_Unknown;

        private XamlMember _member;
        private NullableReference<MethodInfo> _shouldSerializeMethod;

        protected XamlMemberInvoker()
        {
        }

        public XamlMemberInvoker(XamlMember member)
        {
            _member = member ?? throw new ArgumentNullException(nameof(member));
        }

        public static XamlMemberInvoker UnknownInvoker
        {
            get
            {
                if (s_Unknown is null)
                {
                    s_Unknown = new XamlMemberInvoker();
                }

                return s_Unknown;
            }
        }

        public MethodInfo UnderlyingGetter
        {
            get { return IsUnknown ? null : _member.Getter; }
        }

        public MethodInfo UnderlyingSetter
        {
            get { return IsUnknown ? null : _member.Setter; }
        }

        public virtual object GetValue(object instance)
        {
            ArgumentNullException.ThrowIfNull(instance);
            ThrowIfUnknown();
            if (UnderlyingGetter is null)
            {
                throw new NotSupportedException(SR.Format(SR.CantGetWriteonlyProperty, _member));
            }

            if (UnderlyingGetter.IsStatic)
            {
                return UnderlyingGetter.Invoke(null, new object[] { instance });
            }
            else
            {
                return UnderlyingGetter.Invoke(instance, Array.Empty<object>());
            }
        }

        public virtual void SetValue(object instance, object value)
        {
            ArgumentNullException.ThrowIfNull(instance);
            ThrowIfUnknown();
            if (UnderlyingSetter is null)
            {
                throw new NotSupportedException(SR.Format(SR.CantSetReadonlyProperty, _member));
            }

            if (UnderlyingSetter.IsStatic)
            {
                UnderlyingSetter.Invoke(null, new object[] { instance, value });
            }
            else
            {
                UnderlyingSetter.Invoke(instance, new object[] { value });
            }
        }

        internal static XamlMemberInvoker DirectiveInvoker
        {
            get
            {
                if (s_Directive is null)
                {
                    s_Directive = new DirectiveMemberInvoker();
                }

                return s_Directive;
            }
        }

        // Returns true/false if ShouldSerialize method was invoked, null if no method was found
        public virtual ShouldSerializeResult ShouldSerializeValue(object instance)
        {
            if (IsUnknown)
            {
                return ShouldSerializeResult.Default;
            }

            // Look up the ShouldSerializeMethod
            if (!_shouldSerializeMethod.IsSet)
            {
                Type declaringType = _member.UnderlyingMember.DeclaringType;
                string methodName = KnownStrings.ShouldSerialize + _member.Name;
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
                Type[] args;
                if (_member.IsAttachable)
                {
                    args = new Type[] { _member.TargetType.UnderlyingType ?? typeof(object) };
                }
                else
                {
                    flags |= BindingFlags.Instance;
                    args = Type.EmptyTypes;
                }

                _shouldSerializeMethod.Value = declaringType.GetMethod(methodName, flags, null, args, null);
            }

            // Invoke the method if we found one
            MethodInfo shouldSerializeMethod = _shouldSerializeMethod.Value;
            if (shouldSerializeMethod is not null)
            {
                bool result;
                if (_member.IsAttachable)
                {
                    result = (bool)shouldSerializeMethod.Invoke(null, new object[] { instance });
                }
                else
                {
                    result = (bool)shouldSerializeMethod.Invoke(instance, null);
                }

                return result ? ShouldSerializeResult.True : ShouldSerializeResult.False;
            }

            return ShouldSerializeResult.Default;
        }

        private bool IsUnknown
        {
            get { return _member is null || _member.UnderlyingMember is null; }
        }

        private void ThrowIfUnknown()
        {
            if (IsUnknown)
            {
                throw new NotSupportedException(SR.NotSupportedOnUnknownMember);
            }
        }

        private class DirectiveMemberInvoker : XamlMemberInvoker
        {
            public override object GetValue(object instance)
            {
                throw new NotSupportedException(SR.NotSupportedOnDirective);
            }

            public override void SetValue(object instance, object value)
            {
                throw new NotSupportedException(SR.NotSupportedOnDirective);
            }
        }
    }

    public enum ShouldSerializeResult
    {
        Default,
        True,
        False
    }
}
