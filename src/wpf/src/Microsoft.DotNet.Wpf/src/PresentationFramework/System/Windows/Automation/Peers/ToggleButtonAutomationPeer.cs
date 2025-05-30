﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace System.Windows.Automation.Peers
{
    /// 
    public class ToggleButtonAutomationPeer : ButtonBaseAutomationPeer, IToggleProvider
    {
        ///
        public ToggleButtonAutomationPeer(ToggleButton owner): base(owner)
        {}
    
        ///
        protected override string GetClassNameCore()
        {
            return "Button";
        }

        ///
        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.Button;
        }

        /// 
        public override object GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Toggle)
            {
                return this;
            }
            else
            {
                return base.GetPattern(patternInterface);
            }
        }
        
        void IToggleProvider.Toggle()
        {
            if(!IsEnabled())
                throw new ElementNotEnabledException();

            ToggleButton owner = (ToggleButton)Owner;
            owner.OnToggle();
        }

        ToggleState IToggleProvider.ToggleState
        {
            get 
            { 
                ToggleButton owner = (ToggleButton)Owner;
                return ConvertToToggleState(owner.IsChecked); 
            }
        }

        // BUG 1555137: Never inline, as we don't want to unnecessarily link the automation DLL
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal virtual void RaiseToggleStatePropertyChangedEvent(bool? oldValue, bool? newValue)
        {
            if (oldValue != newValue)
            {
                RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, ConvertToToggleState(oldValue), ConvertToToggleState(newValue));
            }
        }

        private static ToggleState ConvertToToggleState(bool? value)
        {
            switch (value)
            {
                case (true):    return ToggleState.On;
                case (false):   return ToggleState.Off;
                default:        return ToggleState.Indeterminate;
            }
        }
    }
}

