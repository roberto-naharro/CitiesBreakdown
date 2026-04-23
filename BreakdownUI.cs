using ColossalFramework.UI;
using UnityEngine;

namespace Breakdown
{
    public class UIBreakdownPanel : UIPanel
    {
        private const int RowCount = 10;
        private static readonly Color32 MutedColor = new Color32(160, 160, 160, 255);

        private UIPanel[] _rows;
        private UILabel[] _fromLabels;
        private UILabel[] _arrowLabels;
        private UILabel[] _toLabels;
        private UILabel[] _tagLabels;
        private UILabel[] _countLabels;

        public override void Start()
        {
            base.Start();
            this.backgroundSprite = "GenericPanel";
            this.color = new Color32(20, 20, 20, 235);
            this.isVisible = false;
            this.relativePosition = new Vector3(parent.width, 0);
            this.isInteractive = false;
            this.name = "BreakdownModPanel";
            this.autoLayout = true;
            this.autoLayoutDirection = LayoutDirection.Vertical;
            this.autoFitChildrenHorizontally = true;
            this.autoFitChildrenVertically = true;

            _rows = new UIPanel[RowCount];
            _fromLabels = new UILabel[RowCount];
            _arrowLabels = new UILabel[RowCount];
            _toLabels = new UILabel[RowCount];
            _tagLabels = new UILabel[RowCount];
            _countLabels = new UILabel[RowCount];

            for (int i = 0; i < RowCount; i++)
            {
                _rows[i] = this.AddUIComponent<UIPanel>();
                _rows[i].autoLayout = true;
                _rows[i].autoLayoutDirection = LayoutDirection.Horizontal;
                _rows[i].autoFitChildrenHorizontally = true;
                _rows[i].autoFitChildrenVertically = true;
                _rows[i].isVisible = false;

                _fromLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _fromLabels[i].padding = new RectOffset(8, 4, 4, 4);

                _arrowLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _arrowLabels[i].text = "→";
                _arrowLabels[i].textColor = MutedColor;
                _arrowLabels[i].padding = new RectOffset(0, 4, 4, 4);
                _arrowLabels[i].isVisible = false;

                _toLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _toLabels[i].padding = new RectOffset(0, 4, 4, 4);
                _toLabels[i].isVisible = false;

                _tagLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _tagLabels[i].text = "(same district)";
                _tagLabels[i].padding = new RectOffset(0, 4, 4, 4);
                _tagLabels[i].isVisible = false;

                _countLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _countLabels[i].textColor = MutedColor;
                _countLabels[i].padding = new RectOffset(0, 8, 4, 4);
            }
        }

        public void SetTopTen(string[] froms, Color32[] fromColors, string[] tos, Color32[] toColors,
            string[] tags, string[] counts, bool showBoth)
        {
            if (_rows == null) return;
            for (int i = 0; i < RowCount; i++)
            {
                if (i < froms.Length)
                {
                    bool hasTag = tags[i] != null;
                    _fromLabels[i].text = froms[i];
                    _fromLabels[i].textColor = fromColors[i];
                    if (hasTag)
                    {
                        _tagLabels[i].text = tags[i];
                        _tagLabels[i].textColor = fromColors[i];
                    }
                    _tagLabels[i].isVisible = hasTag;
                    _arrowLabels[i].isVisible = showBoth && !hasTag;
                    _toLabels[i].isVisible = showBoth && !hasTag;
                    if (showBoth && !hasTag)
                    {
                        _toLabels[i].text = tos[i];
                        _toLabels[i].textColor = toColors[i];
                    }
                    _countLabels[i].text = counts[i];
                    _countLabels[i].isVisible = counts[i] != string.Empty;
                    _rows[i].isVisible = true;
                }
                else
                {
                    _rows[i].isVisible = false;
                }
            }
            this.isVisible = froms.Length > 0;
        }
    }
}
