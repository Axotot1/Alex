﻿using System.Collections.ObjectModel;
using System.Linq;
using Alex.GuiDebugger.Factories;
using Alex.GuiDebugger.Models;
using Alex.GuiDebugger.ViewModels;
using Alex.GuiDebugger.ViewModels.Tools;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dock.Avalonia;
using Dock.Avalonia.Controls;
using Dock.CodeGen;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Serializer;

namespace Alex.GuiDebugger.Views
{
	public class MainWindow : MetroWindow
	{
		private DockJsonSerializer _serializer = new DockJsonSerializer(typeof(ObservableCollection<>));

		public MainWindow()
		{
			this.InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
			this.Closing += (sender, e) =>
			{
				if (this.DataContext is MainWindowViewModel vm)
				{
					if (vm.Layout is IDock dock)
					{
						dock.Close();
					}
				};
			};
			this.FindControl<MenuItem>("FileNew").Click += (sender, e) =>
			{
				if (this.DataContext is MainWindowViewModel vm)
				{
					if (vm.Layout is IDock root)
					{
						root.Close();
					}
					vm.Factory = new DefaultDockFactory(new DockData());
					vm.Layout = vm.Factory.CreateLayout();
					vm.Factory.InitLayout(vm.Layout);
				}
			};

			this.FindControl<MenuItem>("FileOpen").Click += async (sender, e) =>
			{
				var dlg = new OpenFileDialog();
				dlg.Filters.Add(new FileDialogFilter() { Name = "Json", Extensions = { "json" } });
				dlg.Filters.Add(new FileDialogFilter() { Name = "All", Extensions = { "*" } });
				var result = await dlg.ShowAsync(this);
				if (result != null)
				{
					if (this.DataContext is MainWindowViewModel vm)
					{
						IDock layout = _serializer.Load<RootDock>(result.FirstOrDefault());
						if (vm.Layout is IDock root)
						{
							root.Close();
						}
						vm.Layout = layout;
						vm.Factory.InitLayout(vm.Layout);
					}
				}
			};

			this.FindControl<MenuItem>("FileSaveAs").Click += async (sender, e) =>
			{
				var dlg = new SaveFileDialog();
				dlg.Filters.Add(new FileDialogFilter() { Name = "Json", Extensions = { "json" } });
				dlg.Filters.Add(new FileDialogFilter() { Name = "All", Extensions = { "*" } });
				dlg.InitialFileName = "Layout";
				dlg.DefaultExtension = "json";
				var result = await dlg.ShowAsync(this);
				if (result != null)
				{
					if (this.DataContext is MainWindowViewModel vm)
					{
						_serializer.Save(result, vm.Layout);
					}
				}
			};

			this.FindControl<MenuItem>("FileGenerateCode").Click += async (sender, e) =>
			{
				var dlg = new SaveFileDialog();
				dlg.Filters.Add(new FileDialogFilter() { Name = "C#", Extensions = { "cs" } });
				dlg.Filters.Add(new FileDialogFilter() { Name = "All", Extensions = { "*" } });
				dlg.InitialFileName = "ViewModel";
				dlg.DefaultExtension = "cs";
				var result = await dlg.ShowAsync(this);
				if (result != null)
				{
					if (this.DataContext is MainWindowViewModel vm)
					{
						ICodeGen codeGeb = new CSharpCodeGen();
						codeGeb.Generate(vm.Layout, result);
					}
				}
			};

			this.FindControl<MenuItem>("ViewEditor").Click += (sender, e) =>
			{
				if (this.DataContext is MainWindowViewModel vm)
				{
					var editorView = new EditorTool
					{
						Id = "Editor",
						Title = "Editor"
					};

					var layout = new ToolDock
					{
						Id = nameof(IToolDock),
						Proportion = double.NaN,
						Title = nameof(IToolDock),
						CurrentView = editorView,
						DefaultView = editorView,
						Views = vm.Factory.CreateList<IView>
						(
							editorView
						)
					};

					vm.Factory.Update(layout, vm.Layout);

					var window = vm.Factory.CreateWindowFrom(layout);
					if (window != null)
					{
						if (vm.Layout is IDock root)
						{
							vm.Factory.AddWindow(root, window);
						}
						window.X = 0;
						window.Y = 0;
						window.Width = 800;
						window.Height = 600;
						window.Present(false);
					}
				}
			};

			this.FindControl<MenuItem>("OptionsDragBehaviorIsEnabled").Click += (sender, e) =>
			{
				bool isEnabled = (bool)GetValue(DragBehavior.IsEnabledProperty);
				SetValue(DragBehavior.IsEnabledProperty, !isEnabled);
			};

			this.FindControl<MenuItem>("OptionsDropBehaviorIsEnabled").Click += (sender, e) =>
			{
				bool isEnabled = (bool)GetValue(DropBehavior.IsEnabledProperty);
				SetValue(DropBehavior.IsEnabledProperty, !isEnabled);
			};
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}
	}
}
