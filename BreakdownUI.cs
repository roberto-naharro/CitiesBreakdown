using System;
using ColossalFramework.UI;
using UnityEngine;

namespace Breakdown
{
    public class UIBreakdownPanel : UIPanel
    {
        private const int RowCount = 25;

        private UIButton _districtsButton;
        private UIButton _roadsButton;
        private UIButton _averageButton;

        private UILabel    _totalLabel;

        private UIPanel[]  _rows;
        private UILabel[]  _prefixLabels;
        private UILabel[]  _fromLabels;
        private UILabel[]  _arrowLabels;
        private UILabel[]  _toLabels;
        private UILabel[]  _tagLabels;
        private UILabel[]  _countLabels;

        private bool _districtsMode = true;
        private bool _showAverage  = false;

        public Action OnModeToggled;
        public Action OnAverageToggled;

        public override void Awake()
        {
            base.Awake();
            this.canFocus  = false;
            this.isVisible = false;
            // isInteractive left true so header labels can receive clicks
        }

        public override void Start()
        {
            base.Start();
            this.backgroundSprite = "GenericPanel";
            this.color = BreakdownStyle.BgColor;
            this.relativePosition = new Vector3(parent.width, 0);
            parent.eventSizeChanged += (c, v) => { this.relativePosition = new Vector3(parent.width, 0); };
            this.name = "BreakdownModPanel";
            this.autoLayout = true;
            this.autoLayoutDirection = LayoutDirection.Vertical;
            this.autoFitChildrenHorizontally = true;
            this.autoFitChildrenVertically = true;

            // ── Header row ──────────────────────────────────────────────────
            var header = this.AddUIComponent<UIPanel>();
            header.autoLayout = true;
            header.autoLayoutDirection = LayoutDirection.Horizontal;
            header.autoFitChildrenHorizontally = true;
            header.autoFitChildrenVertically = true;

            _districtsButton = header.AddUIComponent<UIButton>();
            _districtsButton.text = "Districts";
            _districtsButton.textScale = BreakdownStyle.TextScale;
            _districtsButton.textColor = BreakdownStyle.ActiveColor;
            _districtsButton.hoveredTextColor = BreakdownStyle.ActiveColor;
            _districtsButton.normalBgSprite = "ButtonSmall";
            _districtsButton.hoveredBgSprite = "ButtonSmallHovered";
            _districtsButton.pressedBgSprite = "ButtonSmallPressed";
            _districtsButton.focusedBgSprite = "ButtonSmall";
            _districtsButton.autoSize = true;
            _districtsButton.canFocus = false;
            _districtsButton.textPadding = new RectOffset(8, 8, 4, 4);
            _districtsButton.eventClick += (c, e) =>
            {
                if (_districtsMode && !_showAverage) return;  // already active tab
                if (_showAverage && OnAverageToggled != null) OnAverageToggled();  // deactivate Average
                if (!_districtsMode)  // mode change needed (was on Roads)
                {
                    _districtsMode = true;
                    if (OnModeToggled != null) OnModeToggled();
                }
                UpdateToggleState();
            };

            _roadsButton = header.AddUIComponent<UIButton>();
            _roadsButton.text = "Roads";
            _roadsButton.textScale = BreakdownStyle.TextScale;
            _roadsButton.textColor = BreakdownStyle.MutedColor;
            _roadsButton.hoveredTextColor = BreakdownStyle.ActiveColor;
            _roadsButton.normalBgSprite = "";
            _roadsButton.hoveredBgSprite = "ButtonSmallHovered";
            _roadsButton.pressedBgSprite = "ButtonSmallPressed";
            _roadsButton.focusedBgSprite = "";
            _roadsButton.autoSize = true;
            _roadsButton.canFocus = false;
            _roadsButton.textPadding = new RectOffset(8, 8, 4, 4);
            _roadsButton.eventClick += (c, e) =>
            {
                if (!_districtsMode && !_showAverage) return;  // already active tab
                if (_showAverage && OnAverageToggled != null) OnAverageToggled();  // deactivate Average
                if (_districtsMode)  // mode change needed (was on Districts)
                {
                    _districtsMode = false;
                    if (OnModeToggled != null) OnModeToggled();
                }
                UpdateToggleState();
            };

            _averageButton = header.AddUIComponent<UIButton>();
            _averageButton.text              = "Average";
            _averageButton.textScale         = BreakdownStyle.TextScale;
            _averageButton.textColor         = BreakdownStyle.MutedColor;
            _averageButton.hoveredTextColor  = BreakdownStyle.ActiveColor;
            _averageButton.normalBgSprite    = "";
            _averageButton.hoveredBgSprite   = "ButtonSmallHovered";
            _averageButton.pressedBgSprite   = "ButtonSmallPressed";
            _averageButton.focusedBgSprite   = "";
            _averageButton.autoSize          = true;
            _averageButton.canFocus          = false;
            _averageButton.textPadding       = new RectOffset(8, 8, 4, 4);
            _averageButton.isEnabled         = false;  // disabled until EMA data is available
            _averageButton.eventClick += (c, e) =>
            {
                if (_showAverage) return;  // already active tab — do nothing
                if (OnAverageToggled != null) OnAverageToggled();
                // UpdateAverageState(true) will be called by ToggleAverageMode to sync visuals
            };

            // ── Total summary row ────────────────────────────────────────────
            _totalLabel = this.AddUIComponent<UILabel>();
            _totalLabel.textScale     = BreakdownStyle.TextScale;
            _totalLabel.textColor     = BreakdownStyle.MutedColor;
            _totalLabel.padding       = new RectOffset(8, 8, 2, 2);
            _totalLabel.autoSize      = true;
            _totalLabel.isInteractive = true;
            _totalLabel.text          = string.Empty;

            // ── Data rows ────────────────────────────────────────────────────
            _rows         = new UIPanel[RowCount];
            _prefixLabels = new UILabel[RowCount];
            _fromLabels   = new UILabel[RowCount];
            _arrowLabels  = new UILabel[RowCount];
            _toLabels     = new UILabel[RowCount];
            _tagLabels    = new UILabel[RowCount];
            _countLabels  = new UILabel[RowCount];

            for (int i = 0; i < RowCount; i++)
            {
                _rows[i] = this.AddUIComponent<UIPanel>();
                _rows[i].autoLayout = true;
                _rows[i].autoLayoutDirection = LayoutDirection.Horizontal;
                _rows[i].autoFitChildrenHorizontally = true;
                _rows[i].autoFitChildrenVertically = true;
                _rows[i].isVisible = false;

                _prefixLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _prefixLabels[i].textColor = BreakdownStyle.MutedColor;
                _prefixLabels[i].textScale = BreakdownStyle.TextScale;
                _prefixLabels[i].padding = new RectOffset(8, 2, 4, 4);
                _prefixLabels[i].isVisible = false;

                _fromLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _fromLabels[i].textScale = BreakdownStyle.TextScale;
                _fromLabels[i].padding = new RectOffset(8, 4, 4, 4);

                _arrowLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _arrowLabels[i].text = "→";
                _arrowLabels[i].textColor = BreakdownStyle.MutedColor;
                _arrowLabels[i].textScale = BreakdownStyle.TextScale;
                _arrowLabels[i].padding = new RectOffset(0, 4, 4, 4);
                _arrowLabels[i].isVisible = false;

                _toLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _toLabels[i].textScale = BreakdownStyle.TextScale;
                _toLabels[i].padding = new RectOffset(0, 4, 4, 4);
                _toLabels[i].isVisible = false;

                _tagLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _tagLabels[i].textScale = BreakdownStyle.TextScale;
                _tagLabels[i].padding = new RectOffset(0, 4, 4, 4);
                _tagLabels[i].isVisible = false;

                _countLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _countLabels[i].textColor = BreakdownStyle.MutedColor;
                _countLabels[i].textScale = BreakdownStyle.TextScale;
                _countLabels[i].padding = new RectOffset(0, 8, 4, 4);
            }
        }

        private void UpdateToggleState()
        {
            if (_districtsButton == null) return;
            bool showDist  = _districtsMode  && !_showAverage;
            bool showRoads = !_districtsMode && !_showAverage;
            _districtsButton.textColor       = showDist  ? BreakdownStyle.ActiveColor : BreakdownStyle.MutedColor;
            _districtsButton.normalBgSprite  = showDist  ? "ButtonSmall" : "";
            _districtsButton.focusedBgSprite = showDist  ? "ButtonSmall" : "";
            _roadsButton.textColor           = showRoads ? BreakdownStyle.ActiveColor : BreakdownStyle.MutedColor;
            _roadsButton.normalBgSprite      = showRoads ? "ButtonSmall" : "";
            _roadsButton.focusedBgSprite     = showRoads ? "ButtonSmall" : "";
            if (_averageButton != null)
            {
                _averageButton.textColor       = _showAverage ? BreakdownStyle.ActiveColor : BreakdownStyle.MutedColor;
                _averageButton.normalBgSprite  = _showAverage ? "ButtonSmall" : "";
                _averageButton.focusedBgSprite = _showAverage ? "ButtonSmall" : "";
            }
        }

        public void UpdateAverageState(bool isAverage)
        {
            _showAverage = isAverage;
            UpdateToggleState();
        }

        public void SetAverageAvailable(bool available)
        {
            if (_averageButton != null) _averageButton.isEnabled = available;
        }

        public void SetTopTen(string[] prefixes, string[] froms, Color32[] fromColors,
            string[] tos, Color32[] toColors, string[] tags, string[] counts, bool[] rowShowBoth,
            bool districtsMode = true, Color32[] countColors = null, string[] tooltips = null,
            string totalText = null, string totalTooltip = null)
        {
            if (_rows == null) return;

            if (_totalLabel != null)
            {
                _totalLabel.text    = totalText ?? string.Empty;
                _totalLabel.tooltip = totalTooltip ?? string.Empty;
            }

            if (districtsMode != _districtsMode)
            {
                _districtsMode = districtsMode;
                UpdateToggleState();
            }

            for (int i = 0; i < RowCount; i++)
            {
                if (i < froms.Length)
                {
                    bool hasTag    = tags[i] != null;
                    bool both      = i < rowShowBoth.Length && rowShowBoth[i];
                    bool hasPrefix = prefixes[i] != string.Empty;

                    _prefixLabels[i].text      = prefixes[i];
                    _prefixLabels[i].isVisible = hasPrefix;
                    _prefixLabels[i].padding   = hasPrefix
                        ? new RectOffset(8, 2, 4, 4)
                        : new RectOffset(0, 0, 0, 0);

                    _fromLabels[i].text      = froms[i];
                    _fromLabels[i].textColor = fromColors[i];
                    _fromLabels[i].padding   = hasPrefix
                        ? new RectOffset(0, 4, 4, 4)
                        : new RectOffset(8, 4, 4, 4);

                    _arrowLabels[i].isVisible = both && !hasTag;

                    _toLabels[i].isVisible = both && !hasTag;
                    if (both && !hasTag)
                    {
                        _toLabels[i].text      = tos[i];
                        _toLabels[i].textColor = toColors[i];
                    }

                    if (hasTag)
                    {
                        _tagLabels[i].text      = tags[i];
                        _tagLabels[i].textColor = fromColors[i];
                    }
                    _tagLabels[i].isVisible = hasTag;

                    _countLabels[i].textColor = (countColors != null && i < countColors.Length) ? countColors[i] : BreakdownStyle.MutedColor;
                    _countLabels[i].text      = counts[i];
                    _countLabels[i].isVisible = counts[i] != string.Empty;

                    _rows[i].isVisible = true;
                    _rows[i].tooltip   = (tooltips != null && i < tooltips.Length && tooltips[i] != null)
                        ? tooltips[i] : string.Empty;
                }
                else
                {
                    _rows[i].isVisible = false;
                    _rows[i].tooltip   = string.Empty;
                }
            }
            this.isVisible = froms.Length > 0;
        }
    }
}
