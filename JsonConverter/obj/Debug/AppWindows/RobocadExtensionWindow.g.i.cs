﻿#pragma checksum "..\..\..\AppWindows\RobocadExtensionWindow.xaml" "{8829d00f-11b8-4213-878b-770e8597ac16}" "1C7952E99310391EE397F480C4035F099D7357EB4DE589611FEA30A21B635336"
//------------------------------------------------------------------------------
// <auto-generated>
//     Этот код создан программой.
//     Исполняемая версия:4.0.30319.42000
//
//     Изменения в этом файле могут привести к неправильной работе и будут потеряны в случае
//     повторной генерации кода.
// </auto-generated>
//------------------------------------------------------------------------------

using JsonConverter.AppWindows;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;


namespace JsonConverter.AppWindows {
    
    
    /// <summary>
    /// RobocadExtensionWindow
    /// </summary>
    public partial class RobocadExtensionWindow : System.Windows.Window, System.Windows.Markup.IComponentConnector {
        
        
        #line 21 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.ComboBox CBLevel;
        
        #line default
        #line hidden
        
        
        #line 26 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox TBBlockNumber;
        
        #line default
        #line hidden
        
        
        #line 28 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBlock TBoxLessonCount;
        
        #line default
        #line hidden
        
        
        #line 29 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox TBLessonCount;
        
        #line default
        #line hidden
        
        
        #line 32 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.CheckBox IsCommon;
        
        #line default
        #line hidden
        
        
        #line 35 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Button BGenerate;
        
        #line default
        #line hidden
        
        private bool _contentLoaded;
        
        /// <summary>
        /// InitializeComponent
        /// </summary>
        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "4.0.0.0")]
        public void InitializeComponent() {
            if (_contentLoaded) {
                return;
            }
            _contentLoaded = true;
            System.Uri resourceLocater = new System.Uri("/JsonConverter;component/appwindows/robocadextensionwindow.xaml", System.UriKind.Relative);
            
            #line 1 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
            System.Windows.Application.LoadComponent(this, resourceLocater);
            
            #line default
            #line hidden
        }
        
        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "4.0.0.0")]
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily")]
        void System.Windows.Markup.IComponentConnector.Connect(int connectionId, object target) {
            switch (connectionId)
            {
            case 1:
            this.CBLevel = ((System.Windows.Controls.ComboBox)(target));
            return;
            case 2:
            this.TBBlockNumber = ((System.Windows.Controls.TextBox)(target));
            return;
            case 3:
            this.TBoxLessonCount = ((System.Windows.Controls.TextBlock)(target));
            return;
            case 4:
            this.TBLessonCount = ((System.Windows.Controls.TextBox)(target));
            return;
            case 5:
            this.IsCommon = ((System.Windows.Controls.CheckBox)(target));
            return;
            case 6:
            this.BGenerate = ((System.Windows.Controls.Button)(target));
            
            #line 36 "..\..\..\AppWindows\RobocadExtensionWindow.xaml"
            this.BGenerate.Click += new System.Windows.RoutedEventHandler(this.BGenerate_Click);
            
            #line default
            #line hidden
            return;
            }
            this._contentLoaded = true;
        }
    }
}

