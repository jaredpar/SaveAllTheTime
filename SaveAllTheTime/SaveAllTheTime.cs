﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text.Editor;
using System.Threading;
using System.Reactive;
using System.Reactive.Linq;
using Microsoft.VisualStudio.Text;
using System.Reactive.Concurrency;
using System.Windows.Forms;
using System.Text;
using Microsoft.VisualStudio.Language.Intellisense;
using System.ComponentModel.Composition;
using EnvDTE;
using Microsoft.VisualStudio.TextManager.Interop;
using SaveAllTheTime.ViewModels;
using SaveAllTheTime.Views;
using System.Reactive.Disposables;
using ReactiveUI;
using SaveAllTheTime.Models;

namespace SaveAllTheTime
{
    public interface IVisualStudioOps
    {
        void SaveAll();
    }

    /// <summary>
    /// Adornment class that draws a square box in the top right hand corner of the viewport
    /// </summary>
    sealed class SaveAllTheTimeAdornment : IDisposable
    {
        readonly IWpfTextView _view;
        readonly IAdornmentLayer _adornmentLayer;

        IDisposable _inner;

        static UserSettings settings;

        static SaveAllTheTimeAdornment()
        {
            // NB: This is a bug in ReactiveUI :-/
            if (MessageBus.Current == null) {
                MessageBus.Current = new MessageBus();
            }
            RxApp.MainThreadScheduler = DispatcherScheduler.Current;
            settings = UserSettings.Load();
            settings.AutoSave();
        }

        /// <summary>
        /// Creates a square image and attaches an event handler to the layout changed event that
        /// adds the the square in the upper right-hand corner of the TextView via the adornment layer
        /// </summary>
        /// <param name="view">The <see cref="IWpfTextView"/> upon which the adornment will be drawn</param>
        public SaveAllTheTimeAdornment(IWpfTextView view, IVisualStudioOps vsOps)
        {
            _view = view;
            _adornmentLayer = view.GetAdornmentLayer("SaveAllTheTimeAdornment");

            var commitControl = new CommitHintView() { 
                ViewModel = new CommitHintViewModel(getFilePathFromView(_view), vsOps, settings),
            };

            var disp = new CompositeDisposable();

            var sizeChanged = Observable.Merge(
                Observable.FromEventPattern<EventHandler, EventArgs>(x => _view.ViewportHeightChanged += x, x => _view.ViewportHeightChanged -= x),
                Observable.FromEventPattern<EventHandler, EventArgs>(x => _view.ViewportWidthChanged += x, x => _view.ViewportWidthChanged -= x));

            var hasAdded = false;
            disp.Add(sizeChanged.Subscribe(x => {
                if (!hasAdded) {
                    _adornmentLayer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, null, commitControl, null);
                    hasAdded = true;
                }

                // NB: The scheduling is to get around initialization where ActualXXX is zero
                var sched = (commitControl.ActualWidth > 0 ? ImmediateScheduler.Instance : RxApp.MainThreadScheduler);
                sched.Schedule(() => {
                    Canvas.SetLeft(commitControl, _view.ViewportRight - commitControl.ActualWidth);
                    Canvas.SetTop(commitControl, _view.ViewportBottom - commitControl.ActualHeight);
                });
            }));

            disp.Add(Disposable.Create(() => _adornmentLayer.RemoveAllAdornments()));

            disp.Add(Observable.FromEventPattern<EventHandler, EventArgs>(x => _view.Closed += x, x => _view.Closed -= x)
                .Subscribe(_ => Dispose()));

            disp.Add(commitControl.ViewModel);
            _inner = disp;
        }

        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref _inner, null);
            if (disp != null) {
                disp.Dispose();
            }
        }

        string getFilePathFromView(IWpfTextView textView)
        {
            var buffer = textView.TextDataModel.DocumentBuffer;
            if (!buffer.Properties.ContainsProperty(typeof(ITextDocument))) return null;

            var doc = buffer.Properties[typeof(ITextDocument)] as ITextDocument;
            if (doc == null) return null;

            return doc.FilePath;
        }
    }
}
