using ColossalFramework.UI;
using UnityEngine;

namespace Breakdown
{
    public class UIBreakdownPanel : UIPanel
    {
        private const int RowCount = 10;
        private static readonly Color32 MutedColor = new Color32(160, 160, 160, 255);

        private UIPanel[] _rows;
        private UILabel[] _nameLabels;
        private UILabel[] _tagLabels;
        private UILabel[] _countLabels;

        public override void Start()
        {
            base.Start();
            this.backgroundSprite = "GenericPanel";
            this.color = new Color32(20, 20, 20, 210);
            this.relativePosition = new Vector3(parent.width, 0);
            this.isInteractive = false;
            this.name = "BreakdownModPanel";
            this.autoLayout = true;
            this.autoLayoutDirection = LayoutDirection.Vertical;
            this.autoFitChildrenHorizontally = true;
            this.autoFitChildrenVertically = true;

            _rows = new UIPanel[RowCount];
            _nameLabels = new UILabel[RowCount];
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

                _nameLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _nameLabels[i].padding = new RectOffset(8, 4, 4, 4);

                _tagLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _tagLabels[i].text = "(same district)";
                _tagLabels[i].padding = new RectOffset(0, 4, 4, 4);
                _tagLabels[i].isVisible = false;

                _countLabels[i] = _rows[i].AddUIComponent<UILabel>();
                _countLabels[i].textColor = MutedColor;
                _countLabels[i].padding = new RectOffset(0, 8, 4, 4);
            }
        }

        public void SetTopTen(string[] names, Color32[] colors, bool[] sameDistrict, string[] counts)
        {
            if (_rows == null) return;
            for (int i = 0; i < RowCount; i++)
            {
                if (i < names.Length)
                {
                    _nameLabels[i].text = names[i];
                    _nameLabels[i].textColor = colors[i];
                    _tagLabels[i].textColor = colors[i];
                    _tagLabels[i].isVisible = sameDistrict[i];
                    _countLabels[i].text = counts[i];
                    _rows[i].isVisible = true;
                }
                else
                {
                    _rows[i].isVisible = false;
                }
            }
            this.isVisible = names.Length > 0;
        }
    }
}
