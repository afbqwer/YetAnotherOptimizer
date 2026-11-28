using RimWorld;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using YaOpt.Defines;
using YaOpt.Helpers;

namespace YaOpt.Settings
{
	public class SettingsPanel
	{
		internal readonly YaOptSettings _settings;

		private enum SettingsTab
		{
			Main,
			Fps,
			Tps,
			Misc
		}

		private SettingsTab _selectedTab = SettingsTab.Main;
		private Vector2 _optionScrollPos = Vector2.zero;
		private Vector2 _descTextScrollPos = Vector2.zero;
		private float _optionViewHeight;
		private OptimizationCategory _categoryFilter = OptimizationCategory.Any;
		private OptimizationOption _lastMouseOverOption = null;
		private Window _lastWindow = null;
		private string _showingDesc = string.Empty;
		private bool _checkOptionChanged = false;

		public SettingsPanel(YaOptSettings settings)
		{
			_settings = settings;
		}

		public void Draw(Rect inRect)
		{
			var currentWindow = Find.WindowStack.currentlyDrawnWindow;
			if (_lastWindow != currentWindow)
			{
				_lastWindow = currentWindow;
				_optionScrollPos = Vector2.zero;
				_descTextScrollPos = Vector2.zero;
				_lastMouseOverOption = null;
				_showingDesc = string.Empty;
			}

			var tabHeader = inRect;
			tabHeader.y += 35f;

			var tabBody = tabHeader;
			tabBody.height -= 40f;
			Widgets.DrawMenuSection(tabBody);

			var list = new List<TabRecord>
			{
				new TabRecord("YaOpt.Setting.Tab.Main".Translate(), delegate
				{
					_optionScrollPos = Vector2.zero;
					_descTextScrollPos = Vector2.zero;
					_lastMouseOverOption = null;
					_showingDesc = string.Empty;
					_categoryFilter = OptimizationCategory.Any;
					_selectedTab = SettingsTab.Main;
				}, _selectedTab == SettingsTab.Main),
				new TabRecord("YaOpt.Setting.Tab.Fps".Translate(), delegate
				{
					_optionScrollPos = Vector2.zero;
					_descTextScrollPos = Vector2.zero;
					_lastMouseOverOption = null;
					_showingDesc = string.Empty;
					_categoryFilter = OptimizationCategory.Fps;
					_selectedTab = SettingsTab.Fps;
				}, _selectedTab == SettingsTab.Fps),
				new TabRecord("YaOpt.Setting.Tab.Tps".Translate(), delegate
				{
					_optionScrollPos = Vector2.zero;
					_descTextScrollPos = Vector2.zero;
					_lastMouseOverOption = null;
					_showingDesc = string.Empty;
					_categoryFilter = OptimizationCategory.Tps;
					_selectedTab = SettingsTab.Tps;
				}, _selectedTab == SettingsTab.Tps),
				new TabRecord("YaOpt.Setting.Tab.Misc".Translate(), delegate
				{
					_optionScrollPos = Vector2.zero;
					_descTextScrollPos = Vector2.zero;
					_lastMouseOverOption = null;
					_showingDesc = string.Empty;
					_categoryFilter = OptimizationCategory.Misc;
					_selectedTab = SettingsTab.Misc;
				}, _selectedTab == SettingsTab.Misc),
			};
			TabDrawer.DrawTabs(tabHeader, list);
			DrawPage(tabBody.ContractedBy(10));

			if (_checkOptionChanged)
			{
				if (!YaOptGlobal.AnyOptionChanged())
				{
					_checkOptionChanged = false;
				}
				else
				{
					var messageRect = new Rect(inRect.x + 5, inRect.yMax + 5, inRect.width * 0.4f, 50);
					Text.Font = GameFont.Tiny;
					Widgets.Label(messageRect, "YaOpt.Setting.Message.RequireReload".Translate());
					Text.Font = GameFont.Small;
				}
			}
		}

		private void DrawPage(Rect inRect)
		{
			inRect.SplitVertically(inRect.width * 0.6f, out var leftRect, out var rightRect);

			Widgets.DrawLineVertical(leftRect.xMax - 5f, leftRect.yMin, leftRect.height);
			leftRect = leftRect.ContractedBy(0, 5);
			leftRect.width -= 25;
			var viewRect = new Rect(0, 0, leftRect.width - 25, _optionViewHeight);
			Widgets.BeginScrollView(leftRect, ref _optionScrollPos, viewRect, true);
			var listing = new Listing_Standard
			{
				verticalSpacing = 4f,
				maxOneColumn = true,
				ColumnWidth = viewRect.width * 0.93f
			};
			listing.Begin(viewRect);
			var lastCategory = string.Empty;
			var lastSubCategory = string.Empty;
			switch (_selectedTab)
			{
				case SettingsTab.Main:
					lastCategory = GetCategoryText(OptimizationCategory.Main);
					break;
				case SettingsTab.Fps:
					lastCategory = GetCategoryText(OptimizationCategory.Fps);
					break;
				case SettingsTab.Tps:
					lastCategory = GetCategoryText(OptimizationCategory.Tps);
					break;
				case SettingsTab.Misc:
					lastCategory = GetCategoryText(OptimizationCategory.Misc);
					break;
			}
			if (CompatibilityChecker.HasProblem)
			{
				leftRect.SplitHorizontally(35f, out var btnRect, out leftRect);
				if (listing.ButtonText("YaOpt.Setting.Button.AutoFixCompatibility".Translate()))
				{
					// Change the options
					CompatibilityChecker.Check(true, true, true, true);
					// Check again
					CompatibilityChecker.Check(false, false, false, true);
					if (CompatibilityChecker.HasProblem)
					{
						Messages.Message("YaOpt.Message.CompatibilityFixFailed".Translate(),
							null, MessageTypeDefOf.CautionInput, false);
					}
					else
					{
						Messages.Message("YaOpt.Message.CompatibilityFixSucceed".Translate(),
							null, MessageTypeDefOf.SilentInput, false);
					}
				}
			}
			foreach (var option in _settings.AllOptimizations)
			{
				if ((_categoryFilter & option.Category) > 0 && option.ShouldShow(_settings))
				{
					var cateText = GetCategoryText(option.Category);
					if (lastCategory != cateText)
					{
						lastCategory = cateText;
						lastSubCategory = string.Empty;
						if (!string.IsNullOrWhiteSpace(cateText))
						{
							Text.Font = GameFont.Medium;
							listing.Label($"<b>{cateText}</b>");
							Text.Font = GameFont.Small;
						}
					}

					var subCateText = !string.IsNullOrWhiteSpace(option.SubCategory) ?
						option.SubCategory.Translate().ToString() :
						string.Empty;
					if (lastSubCategory != subCateText)
					{
						lastSubCategory = subCateText;
						if (!string.IsNullOrWhiteSpace(subCateText))
						{
							listing.Gap(listing.verticalSpacing);
							listing.Label($"<b>{subCateText}</b>");
						}
					}
					DrawOption(listing, option);
				}
			}
			listing.End();
			Widgets.EndScrollView();
			if (Event.current.type == EventType.Layout)
			{
				_optionViewHeight = listing.CurHeight;
			}

			Rect drawRect;
			string btnTextDisable;
			string btnTextEnable;
			OptimizationCategory category;
			switch (_selectedTab)
			{
				case SettingsTab.Main:
					btnTextDisable = "YaOpt.Setting.Button.DisableAll";
					btnTextEnable = "YaOpt.Setting.Button.EnableAll";
					category = OptimizationCategory.Any;
					break;
				case SettingsTab.Fps:
					btnTextDisable = "YaOpt.Setting.Button.DisableAllFps";
					btnTextEnable = "YaOpt.Setting.Button.EnableAllFps";
					category = OptimizationCategory.Fps;
					break;
				case SettingsTab.Tps:
					btnTextDisable = "YaOpt.Setting.Button.DisableAllTps";
					btnTextEnable = "YaOpt.Setting.Button.EnableAllTps";
					category = OptimizationCategory.Tps;
					break;
				case SettingsTab.Misc:
					btnTextDisable = "YaOpt.Setting.Button.DisableAllMisc";
					btnTextEnable = "YaOpt.Setting.Button.EnableAllMisc";
					category = OptimizationCategory.Misc;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
			rightRect.SplitHorizontally(rightRect.height - 35f, out rightRect, out drawRect);
			if (Widgets.ButtonText(drawRect.ContractedBy(30, 2.5f), btnTextDisable.Translate()))
			{
				SetAllOption(false, category);
			}
			rightRect.SplitHorizontally(rightRect.height - 35f, out rightRect, out drawRect);
			if (Widgets.ButtonText(drawRect.ContractedBy(30, 2.5f), btnTextEnable.Translate()))
			{
				SetAllOption(true, category);
			}

			rightRect = rightRect.ContractedBy(10);
			Widgets.LabelScrollable(rightRect, _showingDesc, ref _descTextScrollPos, true, false, true);
		}

		private void DrawOption(Listing_Standard listing, OptimizationOption option)
		{
			var label = option.Name.Translate().ToString();
			var hasNoteS = !string.IsNullOrWhiteSpace(option.NoteStability);
			var hasNoteC = !string.IsNullOrWhiteSpace(option.NoteCompatibility);
			var hasNoteMT = !string.IsNullOrWhiteSpace(option.NoteMultithread);
			// A ban (specific mod) and a patch conflict (default off) share one marker and tooltip.
			var hasConflict = CompatibilityDefines.TryGetConflict(option.SettingId, out var conflictKind);
			// Mark the options that are enabled but could not be installed this session, so the
			// player sees it without having to hover.
			var isSkipped = option == _settings.OptFastListerRemove &&
							ListerThingsHelper.FastRemoveConflict != null;
			// An inert beauty rewrite counts as skipped too: the checkbox is on, the patch is not.
			var isBlocked = option == _settings.OptBeautyFastPath &&
							!string.IsNullOrEmpty(BeautyFastPath.FailReason);
			if (hasNoteS || hasNoteC || hasNoteMT || hasConflict || isSkipped || isBlocked)
			{
				var sb = new StringBuilder(label);
				if (hasNoteS)
					sb.Append(" <color=#FF4040>[S]</color>");
				if (hasNoteC)
					sb.Append(" <color=#DEB0D0>[C]</color>");
				if (hasNoteMT)
					sb.Append(" <color=#0044FF>[MT]</color>");
				if (hasConflict || isSkipped || isBlocked)
					sb.Append(" <color=#FF9020>[!]</color>");
				label = sb.ToString();
			}
			var enabled = option._enabled;
			// Only a hard ban locks the checkbox; a detected patch only lowers the default.
			var disabledByDef = option.IsLockedOff ||
								(hasConflict && conflictKind == ConflictKind.Ban);
			DrawCheckboxLabeled(listing, label, enabled, disabledByDef, out var mouseOver, out var result);
			if (mouseOver && _lastMouseOverOption != option)
			{
				MouseOverOption(option);
			}
			if (result != enabled && !disabledByDef)
			{
				option._enabled = result;
				CheckIfOptionChanged();
			}
			if (option.FuncPostDraw != null)
				option.FuncPostDraw(this, listing, option);
			listing.Gap(listing.verticalSpacing);
		}

		private static void DrawCheckboxLabeled(Listing_Standard listing, string label,
			bool isChecked, bool isDisabled, out bool mouseOver, out bool result, float widthOffset = 0)
		{
			mouseOver = false;
			result = false;
			Rect rect = listing.GetRect(Text.CalcHeight(label, listing.ColumnWidth));
			rect.width += widthOffset;
			//rect.width = Math.Min(rect.width + 24f, listing.ColumnWidth);
			Rect? boundingRectCached = listing.BoundingRectCached;
			if (boundingRectCached.HasValue)
			{
				ref Rect local = ref rect;
				Rect other = boundingRectCached.Value;
				if (!local.Overlaps(other))
				{
					listing.Gap(listing.verticalSpacing);
					return;
				}
			}
			if (Mouse.IsOver(rect))
			{
				Widgets.DrawHighlight(rect);
				mouseOver = true;
			}
			var enabled = isChecked;
			Widgets.CheckboxLabeled(rect, label, ref enabled, isDisabled);
			result = enabled;
		}

		private void MouseOverOption(OptimizationOption option)
		{
			_lastMouseOverOption = option;
			var sb = new StringBuilder();

			if (CompatibilityDefines.TryGetConflict(option.SettingId, out var kind, out var reasons))
			{
				if (kind == ConflictKind.Ban)
				{
					sb.Append("<color=#FF2020>")
						.Append("YaOpt.Setting.Note.Banned".Translate(reasons[0]))
						.AppendLine("</color>");
				}
				else
				{
					sb.Append("<color=#FF9020>")
						.Append("YaOpt.Setting.Note.DefaultOff".Translate())
						.AppendLine("</color>");
					for (var i = 0; i < reasons.Count; i++)
					{
						sb.Append("<color=#FF9020>")
							.Append("YaOpt.Setting.Note.ConflictItem".Translate(reasons[i]))
							.AppendLine("</color>");
					}
				}
			}

			// Turns the option off in the player's eyes only when it really was skipped, not when
			// it is simply unchecked or not evaluated yet (FastRemoveConflict stays null then).
			if (option == _settings.OptFastListerRemove && ListerThingsHelper.FastRemoveConflict != null)
			{
				sb.Append("<color=#FF9020>");
				if (ListerThingsHelper.FastRemoveConflict.Length > 0)
					sb.Append("YaOpt.Setting.Note.FastListerRemove.Skipped"
						.Translate(ListerThingsHelper.FastRemoveConflict));
				else
					sb.Append("YaOpt.Setting.Note.FastListerRemove.SkippedUnknown".Translate());
				sb.AppendLine("</color>");
			}

			// The beauty rewrite decided once, at load, whether it can reproduce the stat's part list.
			// A refusal leaves the option usable but inert, so say so instead of going quiet.
			if (option == _settings.OptBeautyFastPath && !string.IsNullOrEmpty(BeautyFastPath.FailReason))
			{
				sb.Append("<color=#FF4040>")
					.Append("YaOpt.Setting.BeautyFastPath.Blocked".Translate())
					.AppendLine("</color>");
				sb.Append("<color=#FF4040>").Append(BeautyFastPath.FailReason).AppendLine("</color>");
			}

			sb.AppendLine(option.Desc.Translate());

			if (!string.IsNullOrWhiteSpace(option.NoteStability))
			{
				sb.Append("\n\n").Append("<color=#FF4040>").Append("YaOpt.Setting.Note.Stability".Translate()).Append("\n")
					.Append(option.NoteStability.Translate()).Append("</color>");
			}

			if (!string.IsNullOrWhiteSpace(option.NoteCompatibility))
			{
				sb.Append("\n\n").Append("<color=#DEB0D0>").Append("YaOpt.Setting.Note.Compatibility".Translate()).Append("\n")
					.Append(option.NoteCompatibility.Translate()).Append("</color>");
			}

			if (!string.IsNullOrWhiteSpace(option.NoteMultithread))
			{
				sb.Append("\n\n").Append("<color=#0044FF>").Append("YaOpt.Setting.Note.Multithread".Translate()).Append("\n")
					.Append(option.NoteMultithread.Translate()).Append("</color>");
			}
			_showingDesc = sb.ToString();
		}

		public static string GetCategoryText(OptimizationCategory category)
		{
			switch (category)
			{
				case OptimizationCategory.Hidden:
				case OptimizationCategory.Main:
					return string.Empty;
				case OptimizationCategory.Fps: return "YaOpt.Setting.Category.Fps".Translate();
				case OptimizationCategory.Tps: return "YaOpt.Setting.Category.Tps".Translate();
				case OptimizationCategory.Misc: return "YaOpt.Setting.Category.Misc".Translate();
				case OptimizationCategory.Any: return string.Empty;
				default:
					throw new ArgumentOutOfRangeException(nameof(category), category, null);
			}
		}

		private void SetAllOption(bool enable, OptimizationCategory category)
		{
			var filter = enable ? OptimizationFlags.IgnoreEnableAll : OptimizationFlags.IgnoreDisableAll;
			foreach (var optimization in _settings.AllOptimizations)
			{
				if ((optimization.Category & category) > 0 && (optimization.Flags & filter) == 0 &&
					!optimization.IsLockedOff)
				{
					optimization.Enabled = enable;
				}
			}
			CheckIfOptionChanged();
		}

		private void CheckIfOptionChanged()
		{
			if (YaOptGlobal.AnyOptionChanged())
			{
				_checkOptionChanged = true;
			}
		}

		/// <summary>
		/// Prints the reason the beauty rewrite is inert directly under its checkbox, so it is visible
		/// without hovering. The layout check already ran once at load, so this only reads the verdict.
		/// </summary>
		public static void BeautyFastPathPostDraw(SettingsPanel panel, Listing_Standard listing,
			OptimizationOption option)
		{
			var reason = BeautyFastPath.FailReason;
			if (string.IsNullOrEmpty(reason))
				return;

			listing.Indent();
			listing.Gap(1);
			var oldColor = GUI.color;
			GUI.color = new Color(1f, 0.42f, 0.42f);
			listing.Label("YaOpt.Setting.BeautyFastPath.Blocked".Translate());
			listing.Label(reason);
			GUI.color = oldColor;
			listing.Outdent();
		}

		public static void MapMeshUpdateThrottlePostDraw(SettingsPanel panel,
			Listing_Standard listing, OptimizationOption option)
		{
			if (option.Enabled)
			{
				listing.Indent();
				var rect = listing.GetRect(30);
				listing.Gap(-30);
				var result = (int)listing.SliderLabeled(
					"YaOpt.Setting.Option.MapMeshUpdateThrottle.UpdateInterval".Translate(panel._settings.MapMeshUpdateInterval),
					panel._settings.MapMeshUpdateInterval, 100, 1000);
				panel._settings.MapMeshUpdateInterval = result / 100 * 100;
				listing.Outdent();
				if (Mouse.IsOver(rect))
				{
					Widgets.DrawHighlight(rect);
					if (panel._lastMouseOverOption != null)
					{
						panel._lastMouseOverOption = null;
						panel._showingDesc = "YaOpt.Setting.Option.MapMeshUpdateThrottle.UpdateInterval.Desc".Translate();
					}
				}
			}
		}

		public static void IdleThrottlePostDraw(SettingsPanel panel,
			Listing_Standard listing, OptimizationOption option)
		{
			if (option.Enabled)
			{
				listing.Indent();

				#region GetUp

				{
					listing.Gap(listing.verticalSpacing);
					listing.Label("YaOpt.Setting.Option.IdleThrottle.GetUp.Title".Translate());
				}

				{
					listing.Gap(1);
					var oldValue = panel._settings.IdleThrottleGetUpDynamic;
					DrawCheckboxLabeled(listing, "YaOpt.Setting.Option.IdleThrottle.GetUp.Dynamic".Translate(),
						oldValue, false, out var mouseOver, out var result, -12);
					if (mouseOver)
					{
						panel._lastMouseOverOption = null;
						panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.GetUp.Dynamic.Desc".Translate();
					}
					if (oldValue != result)
						panel._settings.IdleThrottleGetUpDynamic = result;
					listing.Gap(5);
				}

				{
					var strTitle = panel._settings.IdleThrottleGetUpDynamic
						? "YaOpt.Setting.Option.IdleThrottle.GetUp.Min"
						: "YaOpt.Setting.Option.IdleThrottle.GetUp";
					var descTitle = panel._settings.IdleThrottleGetUpDynamic
						? "YaOpt.Setting.Option.IdleThrottle.GetUp.Min.Desc"
						: "YaOpt.Setting.Option.IdleThrottle.GetUp.Desc";

					var rect = listing.GetRect(20);
					listing.Gap(-25);
					var result = (int)listing.SliderLabeled(
						strTitle.Translate(panel._settings.IdleThrottleGetUpIntervalMin),
						panel._settings.IdleThrottleGetUpIntervalMin, 211, 1000);
					if (result != 211)
						result = result / 10 * 10;
					panel._settings.IdleThrottleGetUpIntervalMin = result;
					panel._settings.IdleThrottleGetUpIntervalMax = Math.Max(panel._settings.IdleThrottleGetUpIntervalMax, result);
					if (Mouse.IsOver(rect))
					{
						Widgets.DrawHighlight(rect);
						panel._lastMouseOverOption = null;
						panel._showingDesc = descTitle.Translate();
					}
					listing.Gap(-2);
				}

				if (panel._settings.IdleThrottleGetUpDynamic)
				{
					{
						var rect = listing.GetRect(20);
						listing.Gap(-25);
						var result = (int)listing.SliderLabeled(
							"YaOpt.Setting.Option.IdleThrottle.GetUp.Max".Translate(panel._settings.IdleThrottleGetUpIntervalMax),
							panel._settings.IdleThrottleGetUpIntervalMax, 211, 1000);
						if (result != 211)
							result = result / 10 * 10;
						panel._settings.IdleThrottleGetUpIntervalMax = result;
						panel._settings.IdleThrottleGetUpIntervalMin = Math.Min(panel._settings.IdleThrottleGetUpIntervalMin, result);
						if (Mouse.IsOver(rect))
						{
							Widgets.DrawHighlight(rect);
							panel._lastMouseOverOption = null;
							panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.GetUp.Max.Desc".Translate();
						}
						listing.Gap(-2);
					}

					{
						var rect = listing.GetRect(20);
						listing.Gap(-25);
						var result = (int)listing.SliderLabeled(
							"YaOpt.Setting.Option.IdleThrottle.GetUp.PeopleMin".Translate(panel._settings.IdleThrottleGetUpPeopleMin),
							panel._settings.IdleThrottleGetUpPeopleMin, 1, 20);
						panel._settings.IdleThrottleGetUpPeopleMin = result;
						panel._settings.IdleThrottleGetUpPeopleMax = Math.Max(panel._settings.IdleThrottleGetUpPeopleMax, result);
						if (Mouse.IsOver(rect))
						{
							Widgets.DrawHighlight(rect);
							panel._lastMouseOverOption = null;
							panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.GetUp.PeopleMin.Desc".Translate();
						}
						listing.Gap(-2);
					}

					{
						var rect = listing.GetRect(20);
						listing.Gap(-25);
						var result = (int)listing.SliderLabeled(
							"YaOpt.Setting.Option.IdleThrottle.GetUp.PeopleMax".Translate(panel._settings.IdleThrottleGetUpPeopleMax),
							panel._settings.IdleThrottleGetUpPeopleMax, 1, 50);
						panel._settings.IdleThrottleGetUpPeopleMax = result;
						panel._settings.IdleThrottleGetUpPeopleMin = Math.Min(panel._settings.IdleThrottleGetUpPeopleMin, result);
						if (Mouse.IsOver(rect))
						{
							Widgets.DrawHighlight(rect);
							panel._lastMouseOverOption = null;
							panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.GetUp.PeopleMax.Desc".Translate();
						}
					}
				}
				#endregion

				#region StopWander

				{
					listing.Label("YaOpt.Setting.Option.IdleThrottle.StopWander.Title".Translate());
				}

				{
					listing.Gap(1);
					var oldValue = panel._settings.IdleThrottleStopWanderDynamic;
					DrawCheckboxLabeled(listing, "YaOpt.Setting.Option.IdleThrottle.StopWander.Dynamic".Translate(),
						oldValue, false, out var mouseOver, out var result, -12);
					if (mouseOver)
					{
						panel._lastMouseOverOption = null;
						panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.StopWander.Dynamic.Desc".Translate();
					}
					if (oldValue != result)
						panel._settings.IdleThrottleStopWanderDynamic = result;
					listing.Gap(5);
				}

				{
					var strTitle = panel._settings.IdleThrottleStopWanderDynamic
						? "YaOpt.Setting.Option.IdleThrottle.StopWander.Min"
						: "YaOpt.Setting.Option.IdleThrottle.StopWander";
					var descTitle = panel._settings.IdleThrottleStopWanderDynamic
						? "YaOpt.Setting.Option.IdleThrottle.StopWander.Min.Desc"
						: "YaOpt.Setting.Option.IdleThrottle.StopWander.Desc";

					var rect = listing.GetRect(20);
					listing.Gap(-25);
					var result = (int)listing.SliderLabeled(
						strTitle.Translate(panel._settings.IdleThrottleStopWanderIntervalMin),
						panel._settings.IdleThrottleStopWanderIntervalMin, 125, 1000);
					if (result != 125)
						result = result / 10 * 10;
					panel._settings.IdleThrottleStopWanderIntervalMin = result;
					panel._settings.IdleThrottleStopWanderIntervalMax = Math.Max(panel._settings.IdleThrottleStopWanderIntervalMax, result);
					if (Mouse.IsOver(rect))
					{
						Widgets.DrawHighlight(rect);
						panel._lastMouseOverOption = null;
						panel._showingDesc = descTitle.Translate(result, result + 75);
					}
					listing.Gap(-2);
				}

				if (panel._settings.IdleThrottleStopWanderDynamic)
				{
					{
						var rect = listing.GetRect(20);
						listing.Gap(-25);
						var result = (int)listing.SliderLabeled(
							"YaOpt.Setting.Option.IdleThrottle.StopWander.Max".Translate(panel._settings.IdleThrottleStopWanderIntervalMax),
							panel._settings.IdleThrottleStopWanderIntervalMax, 125, 1000);
						if (result != 125)
							result = result / 10 * 10;
						panel._settings.IdleThrottleStopWanderIntervalMax = result;
						panel._settings.IdleThrottleStopWanderIntervalMin = Math.Min(panel._settings.IdleThrottleStopWanderIntervalMin, result);
						if (Mouse.IsOver(rect))
						{
							Widgets.DrawHighlight(rect);
							panel._lastMouseOverOption = null;
							panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.StopWander.Max.Desc"
								.Translate(result, result + 75);
						}
						listing.Gap(-2);
					}

					{
						var rect = listing.GetRect(20);
						listing.Gap(-25);
						var result = (int)listing.SliderLabeled(
							"YaOpt.Setting.Option.IdleThrottle.StopWander.PeopleMin".Translate(panel._settings.IdleThrottleStopWanderPeopleMin),
							panel._settings.IdleThrottleStopWanderPeopleMin, 1, 20);
						panel._settings.IdleThrottleStopWanderPeopleMin = result;
						panel._settings.IdleThrottleStopWanderPeopleMax = Math.Max(panel._settings.IdleThrottleStopWanderPeopleMax, result);
						if (Mouse.IsOver(rect))
						{
							Widgets.DrawHighlight(rect);
							panel._lastMouseOverOption = null;
							panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.StopWander.PeopleMin.Desc".Translate();
						}
						listing.Gap(-2);
					}

					{
						var rect = listing.GetRect(20);
						listing.Gap(-25);
						var result = (int)listing.SliderLabeled(
							"YaOpt.Setting.Option.IdleThrottle.StopWander.PeopleMax".Translate(panel._settings.IdleThrottleStopWanderPeopleMax),
							panel._settings.IdleThrottleStopWanderPeopleMax, 1, 50);
						panel._settings.IdleThrottleStopWanderPeopleMax = result;
						panel._settings.IdleThrottleStopWanderPeopleMin = Math.Min(panel._settings.IdleThrottleStopWanderPeopleMin, result);
						if (Mouse.IsOver(rect))
						{
							Widgets.DrawHighlight(rect);
							panel._lastMouseOverOption = null;
							panel._showingDesc = "YaOpt.Setting.Option.IdleThrottle.StopWander.PeopleMax.Desc".Translate();
						}
					}
				}
				#endregion

				listing.Outdent();
			}
		}
	}
}
