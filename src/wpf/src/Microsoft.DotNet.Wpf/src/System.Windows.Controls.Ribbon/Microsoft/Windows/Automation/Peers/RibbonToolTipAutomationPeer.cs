// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.



#region Using declarations

#if RIBBON_IN_FRAMEWORK
using System.Windows.Controls.Ribbon;

#if RIBBON_IN_FRAMEWORK
namespace System.Windows.Automation.Peers
#else
namespace Microsoft.Windows.Automation.Peers
#endif
{
#else
    using Microsoft.Windows.Controls.Ribbon;
#endif

    #endregion

    public class RibbonToolTipAutomationPeer : ToolTipAutomationPeer
    {
        #region Constructors

        /// <summary>
        ///   Initialize Automation Peer for RibbonToolTip
        /// </summary>
        public RibbonToolTipAutomationPeer(RibbonToolTip owner): base(owner)
        {
        }

        #endregion

        #region AutomationPeer overrides

        /// <summary>
        ///   Return class name for automation clients to display
        /// </summary> 
        protected override string GetClassNameCore()
        {
            return "RibbonToolTip";
        }


        /// <summary>
        ///   Returns name for automation clients to display
        /// </summary>
        protected override string GetNameCore()
        {
            string name = base.GetNameCore();
            if (String.IsNullOrEmpty(name))
            {
                name = ((RibbonToolTip)Owner).Title;
            }

            return name;
        }

        #endregion
    }
}
