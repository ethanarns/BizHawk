﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;

namespace BizHawk.Client.EmuHawk
{
	public partial class LuaConsole : Form, IToolForm
	{
		//TODO:
		//remember column widths and restore column width on restore default settings
		//column click
		//column reorder

		public EmuLuaLibrary LuaImp;

		private int _defaultWidth;
		private int _defaultHeight;
		private LuaFileList _luaList;

		public bool UpdateBefore { get { return true; } }
		public void UpdateValues() { }

		public LuaConsole Get() { return this; }

		public void ConsoleLog(string message)
		{
			OutputBox.Text += message + Environment.NewLine + Environment.NewLine;
			OutputBox.SelectionStart = OutputBox.Text.Length;
			OutputBox.ScrollToCaret();
		}

		public LuaConsole()
		{
			_luaList = new LuaFileList()
			{
				ChangedCallback = SessionChangedCallback,
				LoadCallback = ClearOutputWindow
			};

			InitializeComponent();
			LuaImp = new EmuLuaLibrary(this);
			Closing += (o, e) => SaveConfigSettings();
			LuaListView.QueryItemText += LuaListView_QueryItemText;
			LuaListView.QueryItemBkColor += LuaListView_QueryItemBkColor;
			LuaListView.VirtualMode = true;
		}

		private void LuaConsole_Load(object sender, EventArgs e)
		{
			LoadConfigSettings();
			if (Global.Config.RecentLuaSession.AutoLoad)
			{
				if (!Global.Config.RecentLuaSession.Empty)
				{
					LoadSessionFromRecent(Global.Config.RecentLuaSession[0]);
				}
			}

			NewScriptToolbarItem.Visible = VersionInfo.INTERIM;
			NewScriptMenuItem.Visible = VersionInfo.INTERIM;
		}

		public void Restart()
		{
			_luaList.StopAllScripts();
		}

		public void LoadLuaFile(string path)
		{
			if (LuaAlreadyInSession(path) == false)
			{
				var luaFile = new LuaFile(String.Empty, path);
				_luaList.Add(luaFile);
				LuaListView.ItemCount = _luaList.Count;
				LuaListView.Refresh();
				Global.Config.RecentLua.Add(path);

				if (!Global.Config.DisableLuaScriptsOnLoad)
				{
					try
					{
						luaFile.Thread = LuaImp.SpawnCoroutine(path);
						luaFile.Enabled = true;
					}
					catch (Exception e)
					{
						if (e.ToString().Substring(0, 32) == "LuaInterface.LuaScriptException:")
						{
							luaFile.Enabled = false;
							ConsoleLog(e.Message);
						}
						else MessageBox.Show(e.ToString());
					}
				}
				else luaFile.Enabled = false;
				luaFile.Paused = false;
			}
			else
			{
				foreach (var file in _luaList)
				{
					if (path == file.Path && file.Enabled == false && !Global.Config.DisableLuaScriptsOnLoad)
					{
						file.Toggle();
						break;
					}
				}

				RunLuaScripts();
				UpdateDialog();
			}
		}

		public void UpdateDialog()
		{
			LuaListView.ItemCount = _luaList.Count;
			UpdateNumberOfScripts();
		}

		public void RunLuaScripts()
		{
			for (var i = 0; i < _luaList.Count; i++)
			{
				if (_luaList[i].Enabled && _luaList[i].Thread == null)
				{
					try
					{
						_luaList[i].Thread = LuaImp.SpawnCoroutine(_luaList[i].Path);
					}
					catch (Exception e)
					{
						if (e.ToString().Substring(0, 32) == "LuaInterface.LuaScriptException:")
						{
							_luaList[i].Enabled = false;
							ConsoleLog(e.Message);
						}
						else MessageBox.Show(e.ToString());
					}
				}
				else
				{
					_luaList[i].Stop();
					_luaList.Changes = true;
				}
			}
		}

		private void SessionChangedCallback()
		{
			OutputMessages.Text =
				(_luaList.Changes ? "* " : String.Empty) + 
				Path.GetFileName(_luaList.Filename);
		}

		private void LuaListView_QueryItemBkColor(int index, int column, ref Color color)
		{
			if (column == 0)
			{
				if (_luaList[index].IsSeparator)
				{
					color = BackColor;
				}
				else if (_luaList[index].Enabled && !_luaList[index].Paused)
				{
					color = Color.LightCyan;
				}
				else if (_luaList[index].Enabled && _luaList[index].Paused)
				{
					color = Color.IndianRed;
				}
			}

			UpdateNumberOfScripts();
		}

		private void LuaListView_QueryItemText(int index, int column, out string text)
		{
			text = String.Empty;

			if (column == 0)
			{
				text = Path.GetFileNameWithoutExtension(_luaList[index].Path); //TODO: how about a list of Names and allow the user to name them?
			}
			if (column == 1)
			{
				text = _luaList[index].Path;
			}
		}

		private void SaveConfigSettings()
		{
			LuaImp.Close();
			Global.Config.LuaConsoleWndx = Location.X;
			Global.Config.LuaConsoleWndy = Location.Y;
			Global.Config.LuaConsoleWidth = Right - Left;
			Global.Config.LuaConsoleHeight = Bottom - Top;
		}

		private void LoadConfigSettings()
		{
			_defaultWidth = Size.Width;		//Save these first so that the user can restore to its original size
			_defaultHeight = Size.Height;

			if (Global.Config.LuaConsoleSaveWindowPosition && Global.Config.LuaConsoleWndx >= 0 && Global.Config.LuaConsoleWndy >= 0)
				Location = new Point(Global.Config.LuaConsoleWndx, Global.Config.LuaConsoleWndy);

			if (Global.Config.LuaConsoleWidth >= 0 && Global.Config.LuaConsoleHeight >= 0)
			{
				Size = new Size(Global.Config.LuaConsoleWidth, Global.Config.LuaConsoleHeight);
			}
		}

		private FileInfo GetFileFromUser(string filter)
		{
			var ofd = new OpenFileDialog();
			ofd.InitialDirectory = PathManager.GetLuaPath();
			ofd.Filter = filter;
			ofd.RestoreDirectory = true;


			if (!Directory.Exists(ofd.InitialDirectory))
				Directory.CreateDirectory(ofd.InitialDirectory);

			GlobalWin.Sound.StopSound();
			var result = ofd.ShowDialog();
			GlobalWin.Sound.StartSound();
			if (result != DialogResult.OK)
				return null;
			var file = new FileInfo(ofd.FileName);
			return file;
		}

		private void UpdateNumberOfScripts()
		{
			var message = String.Empty;
			int active = 0, paused = 0, separators = 0;
			foreach (var file in _luaList)
			{
				if (!file.IsSeparator)
				{
					if (file.Enabled)
					{
						active++;
						if (file.Paused)
						{
							paused++;
						}
					}
				}
				else
				{
					separators++;
				}
			}

			var L = _luaList.Count - separators;

			if (L == 1)
			{
				message += L + " script (" + active + " active, " + paused + " paused)";
			}
			else if (L == 0)
			{
				message += L + " scripts";
			}
			else
			{
				message += L + " scripts (" + active + " active, " + paused + " paused)";
			}

			NumberOfScripts.Text = message;
		}

		private void LoadLuaFromRecent(string path)
		{
			LoadLuaFile(path);
		}

		private bool LuaAlreadyInSession(string path)
		{
			return _luaList.Any(t => path == t.Path);
		}

		public void WriteToOutputWindow(string message)
		{
			if (!OutputBox.IsHandleCreated || OutputBox.IsDisposed)
				return;

			OutputBox.Invoke(() =>
			{
				OutputBox.Text += message + "\n\n";
				OutputBox.SelectionStart = OutputBox.Text.Length;
				OutputBox.ScrollToCaret();
			});
		}

		public void ClearOutputWindow()
		{
			if (!OutputBox.IsHandleCreated || OutputBox.IsDisposed)
				return;

			OutputBox.Invoke(() =>
			{
				OutputBox.Text = String.Empty;
				OutputBox.Refresh();
			});
		}

		public bool LoadLuaSession(string path)
		{
			return _luaList.LoadLuaSession(path);
		}

		/// <summary>
		/// resumes suspended coroutines
		/// </summary>
		/// <param name="includeFrameWaiters">should frame waiters be waken up? only use this immediately before a frame of emulation</param>
		public void ResumeScripts(bool includeFrameWaiters)
		{
			if (_luaList != null && _luaList.Count > 0)
			{
				if (LuaImp.GuiLibrary.SurfaceIsNull)
				{
					LuaImp.GuiLibrary.DrawNewEmu();
				}
				foreach (var lf in _luaList)
				{
					//save old current directory before this lua thread clobbers it for the .net thread
					var oldcd = Environment.CurrentDirectory;

					try
					{
						//LuaImp.gui_clearGraphics();
						if (lf.Enabled && lf.Thread != null && !(lf.Paused))
						{
							var prohibit = lf.FrameWaiting && !includeFrameWaiters;
							if (!prohibit)
							{
								//restore this lua thread's preferred current directory
								if (lf.CurrentDirectory != null)
								{
									Environment.CurrentDirectory = lf.CurrentDirectory;
								}
								var result = LuaImp.ResumeScript(lf.Thread);
								if (result.Terminated) lf.Stop();
								lf.FrameWaiting = result.WaitForFrame;

								//if the lua thread changed its current directory, capture that here
								lf.CurrentDirectory = Environment.CurrentDirectory;
							}
						}
					}
					catch (Exception ex)
					{
						if (ex is LuaInterface.LuaScriptException || ex is LuaInterface.LuaException)
						{
							lf.Enabled = false;
							lf.Thread = null;
							ConsoleLog(ex.ToString());
						}
						else MessageBox.Show(ex.ToString());
					}

					//restore the current directory
					Environment.CurrentDirectory = oldcd;
				}
			}
			//LuaImp.gui_drawFinishEmu();
		}

		public void StartLuaDrawing()
		{
			if (_luaList.Any())
			{
				if (LuaImp.GuiLibrary.SurfaceIsNull)
				{
					LuaImp.GuiLibrary.DrawNewEmu();
				}
			}
		}

		public void EndLuaDrawing()
		{
			if (_luaList.Any())
			{
				LuaImp.GuiLibrary.DrawFinishEmu();
			}
		}

		public bool WaitOne(int timeout)
		{
			if (!IsHandleCreated || IsDisposed)
			{
				return true;
			}

			return LuaImp.LuaWait.WaitOne(timeout);
		}

		private FileInfo GetSaveFileFromUser()
		{
			var sfd = new SaveFileDialog();
			if (!String.IsNullOrWhiteSpace(_luaList.Filename))
			{
				sfd.FileName = Path.GetFileNameWithoutExtension(_luaList.Filename);
				sfd.InitialDirectory = Path.GetDirectoryName(_luaList.Filename);
			}
			else if (!(Global.Emulator is NullEmulator))
			{
				sfd.FileName = PathManager.FilesystemSafeName(Global.Game);
				sfd.InitialDirectory = PathManager.GetLuaPath();
			}
			else
			{
				sfd.FileName = "NULL";
				sfd.InitialDirectory = PathManager.GetLuaPath();
			}
			sfd.Filter = "Lua Session Files (*.luases)|*.luases|All Files|*.*";
			sfd.RestoreDirectory = true;
			GlobalWin.Sound.StopSound();
			var result = sfd.ShowDialog();
			GlobalWin.Sound.StartSound();
			if (result != DialogResult.OK)
				return null;
			var file = new FileInfo(sfd.FileName);
			return file;
		}

		private void SaveSessionAs()
		{
			var file = GetSaveFileFromUser();
			if (file != null)
			{
				_luaList.SaveSession(file.FullName);
				OutputMessages.Text = Path.GetFileName(_luaList.Filename) + " saved.";
			}
		}

		public void LoadSessionFromRecent(string path)
		{
			var doload = true;
			if (_luaList.Changes) doload = AskSave();

			if (doload)
			{
				if (!_luaList.LoadLuaSession(path))
				{
					ToolHelpers.HandleLoadError(Global.Config.RecentLuaSession, path);
				}
				else
				{
					RunLuaScripts();
					UpdateDialog();
					_luaList.Changes = false;
				}
			}
		}

		public bool AskSave()
		{
			if (Global.Config.SupressAskSave)
			{
				return true;
			}

			if (_luaList.Changes)
			{
				GlobalWin.Sound.StopSound();
				var result = MessageBox.Show("Save changes to session?", "Lua Console", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
				GlobalWin.Sound.StartSound();
				if (result == DialogResult.Yes)
				{
					if (!String.IsNullOrWhiteSpace(_luaList.Filename))
					{
						_luaList.SaveSession();
					}
					else
					{
						SaveSessionAs();
					}

					return true;
				}
				else if (result == DialogResult.No)
				{
					return true;
				}
				else if (result == DialogResult.Cancel)
				{
					return false;
				}
			}
			return true;
		}

		private static void OpenLuaWriter(string path)
		{
			var writer = new LuaWriter {CurrentFile = path};
			writer.Show();
		}

		private Point GetPromptPoint()
		{
			return PointToScreen(
				new Point(LuaListView.Location.X + 30, LuaListView.Location.Y + 30)
			);
		}

		private IEnumerable<int> SelectedIndices
		{
			get { return LuaListView.SelectedIndices.Cast<int>(); }
		}

		private IEnumerable<LuaFile> SelectedItems
		{
			get { return SelectedIndices.Select(index => _luaList[index]); }
		}

		private IEnumerable<LuaFile> SelectedFiles
		{
			get { return SelectedItems.Where(x => !x.IsSeparator); }
		}

		#region Events

		#region File Menu

		private void FileSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			SaveSessionMenuItem.Enabled = _luaList.Changes;
		}

		private void RecentSessionsSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			RecentSessionsSubMenu.DropDownItems.Clear();
			RecentSessionsSubMenu.DropDownItems.AddRange(
				ToolHelpers.GenerateRecentMenu(Global.Config.RecentLuaSession, LoadSessionFromRecent)
			);
		}

		private void RecentScriptsSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			RecentScriptsSubMenu.DropDownItems.Clear();
			RecentScriptsSubMenu.DropDownItems.AddRange(
				ToolHelpers.GenerateRecentMenu(Global.Config.RecentLua, LoadLuaFromRecent)
			);
		}

		private void NewSessionMenuItem_Click(object sender, EventArgs e)
		{
			var result = !_luaList.Changes || AskSave();

			if (result)
			{
				_luaList.Clear();
				ClearOutputWindow();
				UpdateDialog();
			}
		}

		private void OpenSessionMenuItem_Click(object sender, EventArgs e)
		{
			var file = GetFileFromUser("Lua Session Files (*.luases)|*.luases|All Files|*.*");
			if (file != null)
			{
				_luaList.LoadLuaSession(file.FullName);
				RunLuaScripts();
				UpdateDialog();
				_luaList.Changes = false;
			}
		}

		private void SaveSessionMenuItem_Click(object sender, EventArgs e)
		{
			if (_luaList.Changes)
			{
				if (!String.IsNullOrWhiteSpace(_luaList.Filename))
				{
					_luaList.SaveSession();
				}
				else
				{
					SaveSessionAs();
				}

				OutputMessages.Text = Path.GetFileName(_luaList.Filename) + " saved.";
			}
		}

		private void SaveSessionAsMenuItem_Click(object sender, EventArgs e)
		{
			SaveSessionAs();
		}

		private void ExitMenuItem_Click(object sender, EventArgs e)
		{
			Close();
		}

		#endregion

		#region Script

		private void ScriptSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			ToggleScriptMenuItem.Enabled =
				PauseScriptMenuItem.Enabled =
				EditScriptMenuItem.Enabled =
				SelectedFiles.Any();

			RemoveScriptMenuItem.Enabled =
				MoveUpMenuItem.Enabled =
				MoveDownMenuItem.Enabled =
				SelectedIndices.Any();

			SelectAllMenuItem.Enabled = _luaList.Any();
			StopAllScriptsMenuItem.Enabled = _luaList.Any(script => script.Enabled);
			RegisteredFunctionsMenuItem.Enabled = GlobalWin.Tools.LuaConsole.LuaImp.RegisteredFunctions.Any();

			NewScriptMenuItem.Visible = VersionInfo.INTERIM;
		}

		private void NewScriptMenuItem_Click(object sender, EventArgs e)
		{
			OpenLuaWriter(null);
		}

		private void OpenScriptMenuItem_Click(object sender, EventArgs e)
		{
			var file = GetFileFromUser("Lua Scripts (*.lua)|*.lua|Text (*.text)|*.txt|All Files|*.*");
			if (file != null)
			{
				LoadLuaFile(file.FullName);
				UpdateDialog();
			}
		}

		private void ToggleScriptMenuItem_Click(object sender, EventArgs e)
		{
			foreach (var item in SelectedFiles)
			{
				item.Toggle();

				if (item.Enabled && item.Thread == null)
				{
					try
					{
						item.Thread = LuaImp.SpawnCoroutine(item.Path);
					}
					catch (Exception ex)
					{
						if (ex.ToString().Substring(0, 32) == "LuaInterface.LuaScriptException:")
						{
							item.Enabled = false;
							ConsoleLog(ex.Message);
						}
						else
						{
							MessageBox.Show(ex.ToString());
						}
					}
				}
				else if (!item.Enabled && item.Thread != null)
				{
					item.Stop();
				}
			}

			LuaListView.Refresh();
			_luaList.Changes = true;
		}

		private void PauseScriptMenuItem_Click(object sender, EventArgs e)
		{
			SelectedFiles.ToList().ForEach(x => x.TogglePause());
			LuaListView.Refresh();
		}

		private void EditScriptMenuItem_Click(object sender, EventArgs e)
		{
			SelectedFiles.ToList().ForEach(file => System.Diagnostics.Process.Start(file.Path));
		}

		private void RemoveScriptMenuItem_Click(object sender, EventArgs e)
		{
			if (SelectedItems.Any())
			{
				foreach (var item in SelectedItems)
				{
					_luaList.Remove(item);
				}
				
				UpdateDialog();
			}
		}

		private void InsertSeparatorMenuItem_Click(object sender, EventArgs e)
		{
			var indices = SelectedIndices.ToList();
			if (indices.Any() && indices.Last() < _luaList.Count)
			{
				_luaList.Insert(indices.Last(), LuaFile.SeparatorInstance);
			}
			else
			{
				_luaList.Add(LuaFile.SeparatorInstance);
			}

			UpdateDialog();
		}

		private void MoveUpMenuItem_Click(object sender, EventArgs e)
		{
			var indices = SelectedIndices.ToList();
			if (indices.Count == 0 || indices[0] == 0)
			{
				return;
			}

			foreach (var index in indices)
			{
				var file = _luaList[index];
				_luaList.Remove(file);
				_luaList.Insert(index - 1, file);
			}

			var newindices = indices.Select(t => t - 1).ToList();

			LuaListView.SelectedIndices.Clear();
			foreach (var newi in newindices)
			{
				LuaListView.SelectItem(newi, true);
			}

			UpdateDialog();
		}

		private void MoveDownMenuItem_Click(object sender, EventArgs e)
		{
			var indices = SelectedIndices.ToList();
			if (indices.Count == 0 || indices.Last() == _luaList.Count - 1)
			{
				return;
			}

			foreach (var index in indices)
			{
				var file = _luaList[index];
				_luaList.Remove(file);
				_luaList.Insert(index + 1, file);
			}

			var newindices = indices.Select(t => t + 1).ToList();

			LuaListView.SelectedIndices.Clear();
			foreach (var newi in newindices)
			{
				LuaListView.SelectItem(newi, true);
			}

			UpdateDialog();
		}

		private void SelectAllMenuItem_Click(object sender, EventArgs e)
		{
			for (var i = 0; i < _luaList.Count; i++)
			{
				LuaListView.SelectItem(i, true);
			}
		}

		private void StopAllScriptsMenuItem_Click(object sender, EventArgs e)
		{
			_luaList.StopAllScripts();
		}

		private void RegisteredFunctionsMenuItem_Click(object sender, EventArgs e)
		{
			if (LuaImp.RegisteredFunctions.Any())
			{
				var alreadyOpen = false;
				foreach (Form form in Application.OpenForms)
				{
					if (form is LuaRegisteredFunctionsList)
					{
						alreadyOpen = true;
						form.Focus();
					}
				}

				if (!alreadyOpen)
				{
					var form = new LuaRegisteredFunctionsList {StartLocation = GetPromptPoint()};
					form.Show();
				}
			}
		}

		#endregion

		#region Options

		private void OptionsSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			SaveWindowPositionMenuItem.Checked = Global.Config.LuaConsoleSaveWindowPosition;
			AutoloadConsoleMenuItem.Checked = Global.Config.AutoLoadLuaConsole;
			AutoloadSessionMenuItem.Checked = Global.Config.RecentLuaSession.AutoLoad;
			DisableScriptsOnLoadMenuItem.Checked = Global.Config.DisableLuaScriptsOnLoad;
		}

		private void SaveWindowPositionMenuItem_Click(object sender, EventArgs e)
		{
			Global.Config.LuaConsoleSaveWindowPosition ^= true;
		}

		private void AutoloadConsoleMenuItem_Click(object sender, EventArgs e)
		{
			Global.Config.AutoLoadLuaConsole ^= true;
		}

		private void AutoloadSessionMenuItem_Click(object sender, EventArgs e)
		{
			Global.Config.RecentLuaSession.AutoLoad ^= true;
		}

		private void DisableScriptsOnLoadMenuItem_Click(object sender, EventArgs e)
		{
			Global.Config.DisableLuaScriptsOnLoad ^= true;
		}

		private void RestoreDefaultSettingsMenuItem_Click(object sender, EventArgs e)
		{
			Size = new Size(_defaultWidth, _defaultHeight);
		}

		#endregion

		#region Help

		private void FunctionsListMenuItem_Click(object sender, EventArgs e)
		{
			GlobalWin.Sound.StopSound();
			new LuaFunctionsForm().Show();
			GlobalWin.Sound.StartSound();
		}

		private void OnlineDocsMenuItem_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start("http://tasvideos.org/BizHawk/LuaFunctions.html");
		}

		#endregion

		#region Toolbar and Context Menu

		private void EditToolbarItem_Click(object sender, EventArgs e)
		{
			if (VersionInfo.INTERIM)
			{
				SelectedFiles.ToList().ForEach(x => OpenLuaWriter(x.Path));
			}
			else
			{
				EditScriptMenuItem_Click(sender, e);
			}
		}

		private void ScriptListContextMenu_Opening(object sender, CancelEventArgs e)
		{
			ToggleScriptContextItem.Enabled =
				PauseScriptContextItem.Enabled =
				EditScriptContextItem.Enabled =
				SelectedFiles.Any();

			StopAllScriptsContextItem.Visible =
				ScriptContextSeparator.Visible =
				_luaList.Any(file => file.Enabled);
		}

		private void ConsoleContextMenu_Opening(object sender, CancelEventArgs e)
		{
			RegisteredFunctionsContextItem.Enabled = LuaImp.RegisteredFunctions.Any();
		}

		private void ClearConsoleContextItem_Click(object sender, EventArgs e)
		{
			ClearOutputWindow();
		}

		#endregion

		#region Dialog, Listview, OutputBox

		private void LuaConsole_DragDrop(object sender, DragEventArgs e)
		{
			var filePaths = (string[])e.Data.GetData(DataFormats.FileDrop);
			try
			{
				foreach (var path in filePaths)
				{
					if (Path.GetExtension(path) == (".lua") || Path.GetExtension(path) == (".txt"))
					{
						LoadLuaFile(path);
						UpdateDialog();
					}
					else if (Path.GetExtension(path) == (".luases"))
					{
						_luaList.LoadLuaSession(path);
						RunLuaScripts();
						UpdateDialog();
						_luaList.Changes = false;
						return;
					}
				}
			}
			catch (Exception ex)
			{
				if (ex.ToString().Substring(0, 32) == "LuaInterface.LuaScriptException:" || ex.ToString().Substring(0, 26) == "LuaInterface.LuaException:")
				{
					ConsoleLog(ex.Message);
				}
				else
				{
					MessageBox.Show(ex.Message);
				}
			}
		}

		private void LuaConsole_DragEnter(object sender, DragEventArgs e)
		{
			e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		}

		private void LuaListView_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Delete && !e.Control && !e.Alt && !e.Shift)
			{
				RemoveScriptMenuItem_Click(null, null);
			}
			else if (e.KeyCode == Keys.A && e.Control && !e.Alt && !e.Shift) //Select All
			{
				SelectAllMenuItem_Click(null, null);
			}
			else if (e.KeyCode == Keys.F12 && !e.Control && !e.Alt && !e.Shift) //F12
			{
				RegisteredFunctionsMenuItem_Click(null, null);
			}
		}

		private void LuaListView_ItemActivate(object sender, EventArgs e)
		{
			ToggleScriptMenuItem_Click(sender, e);
		}

		private void OutputBox_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.F12 && !e.Control && !e.Alt && !e.Shift) //F12
			{
				RegisteredFunctionsMenuItem_Click(null, null);
			}
		}

		#endregion

		#endregion
	}
}
