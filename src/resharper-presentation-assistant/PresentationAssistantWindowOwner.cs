﻿using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using JetBrains.Application;
using JetBrains.DataFlow;
using JetBrains.Interop.WinApi;
using JetBrains.Threading;
using JetBrains.UI.PopupWindowManager;
using JetBrains.UI.Theming;

namespace JetBrains.ReSharper.Plugins.PresentationAssistant
{
    [ShellComponent]
    public class PresentationAssistantWindowOwner
    {
        private static readonly TimeSpan VisibleTimeSpan = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan TopmostTimeSpan = TimeSpan.FromMilliseconds(200);

        private readonly IThreading threading;
        private readonly PresentationAssistantPopupWindowContext context;
        private readonly PopupWindowManager popupWindowManager;
        private readonly ITheming theming;
        private Action<Shortcut> showAction;

        public PresentationAssistantWindowOwner(Lifetime lifetime, IThreading threading,
            PresentationAssistantPopupWindowContext context, PopupWindowManager popupWindowManager, ITheming theming)
        {
            this.threading = threading;
            this.context = context;
            this.popupWindowManager = popupWindowManager;
            this.theming = theming;

            Enabled = new Property<bool>(lifetime, "PresentationAssistantWindow::Enabled");
            Enabled.WhenTrue(lifetime, EnableShortcuts);
        }

        public IProperty<bool> Enabled { get; private set; }

        public void Show(Shortcut shortcut)
        {
            showAction(shortcut);
        }

        private void EnableShortcuts(Lifetime enabledLifetime)
        {
            var popupWindowLifetimeDefinition = Lifetimes.Define(enabledLifetime, "PresentationAssistant::PopupWindow");

            var window = new PresentationAssistantWindow();
            var windowInteropHelper = new WindowInteropHelper(window) { Owner = context.MainWindow.Handle };

            theming.PopulateResourceDictionary(popupWindowLifetimeDefinition, window.Resources);

            var popupWindow = new FadingWpfPopupWindow(popupWindowLifetimeDefinition, context, context.Mutex,
                popupWindowManager, window, opacity: 0.8);

            var visibilityLifetimes = new SequentialLifetimes(popupWindowLifetimeDefinition);

            showAction = shortcut =>
            {
                window.SetShortcut(shortcut);
                popupWindow.ShowWindow();

                visibilityLifetimes.DefineNext((visibleLifetimeDefinition, visibleLifetime) =>
                {
                    // Hide the window after a timespan, and queue it on the visible sequential lifetime so
                    // that the timer is cancelled when we want to show a new shortcut. Don't hide the window
                    // by attaching it to the sequential lifetime, as that will cause the window to hide when
                    // we need to show a new shortcut. But, make sure we do terminate the lifetime so that
                    // the topmost timer hack is stopped when this window is not visible
                    threading.TimedActions.Queue(visibleLifetime, "PresentationAssistantWindow::HideWindow",
                        () => popupWindow.HideWindow(), VisibleTimeSpan,
                        TimedActionsHost.Recurrence.OneTime, Rgc.Invariant);
                });
            };

            enabledLifetime.AddAction(() =>
            {
                showAction = _ => { };
            });
        }
    }
}