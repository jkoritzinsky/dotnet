﻿// <auto-generated/>
#pragma warning disable 1591
namespace Test
{
    #line default
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Threading.Tasks;
    using global::Microsoft.AspNetCore.Components;
    #line default
    #line hidden
    #nullable restore
    public partial class TestComponent : global::Microsoft.AspNetCore.Components.ComponentBase
    #nullable disable
    {
        #pragma warning disable 1998
        protected override void BuildRenderTree(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)
        {
            {
                global::__Blazor.Test.TestComponent.TypeInference.CreateGrid_0_CaptureParameters(
#nullable restore
#line (1,15)-(1,48) "x:\dir\subdir\Test\TestComponent.cshtml"
() => new Dictionary<X, string>()

#line default
#line hidden
#nullable disable
                , out var __typeInferenceArg_0___arg0);
                global::__Blazor.Test.TestComponent.TypeInference.CreateGrid_0(__builder, 0, 1, __typeInferenceArg_0___arg0, 2, (__builder2) => {
                    global::__Blazor.Test.TestComponent.TypeInference.CreateGridColumn_1(__builder2, 3, __typeInferenceArg_0___arg0);
                }
                );
                __typeInferenceArg_0___arg0 = default;
            }
        }
        #pragma warning restore 1998
    }
}
namespace __Blazor.Test.TestComponent
{
    #line hidden
    internal static class TypeInference
    {
        public static void CreateGrid_0<T>(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder, int seq, int __seq0, global::System.Func<global::System.Collections.Generic.Dictionary<global::X, T>> __arg0, int __seq1, Microsoft.AspNetCore.Components.RenderFragment __arg1)
        {
        __builder.OpenComponent<global::Test.Grid<T>>(seq);
        __builder.AddComponentParameter(__seq0, nameof(global::Test.Grid<T>.
#nullable restore
#line (1,7)-(1,11) "x:\dir\subdir\Test\TestComponent.cshtml"
Data

#line default
#line hidden
#nullable disable
        ), __arg0);
        __builder.AddComponentParameter(__seq1, "ChildContent", __arg1);
        __builder.CloseComponent();
        }

        public static void CreateGrid_0_CaptureParameters<T>(global::System.Func<global::System.Collections.Generic.Dictionary<global::X, T>> __arg0, out global::System.Func<global::System.Collections.Generic.Dictionary<global::X, T>> __arg0_out)
        {
            __arg0_out = __arg0;
        }
        public static void CreateGridColumn_1<T>(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder, int seq, global::System.Func<global::System.Collections.Generic.Dictionary<global::X, T>> __syntheticArg0)
        {
        __builder.OpenComponent<global::Test.GridColumn<T>>(seq);
        __builder.CloseComponent();
        }
    }
}
#pragma warning restore 1591
