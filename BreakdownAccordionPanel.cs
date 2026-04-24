using System;
using ColossalFramework.UI;
using UnityEngine;

namespace Breakdown
{
    public class DistrictSection
    {
        public string         name;
        public Color32        color;
        public uint           totalRoutes;
        public ConnectionData[] connections;
    }

    public struct ConnectionData
    {
        public string  name;
        public Color32 color;
        public uint    routes;
        public string  tooltip;
    }

    public class UIBreakdownAccordionPanel : UIPanel
    {
        public const int MaxSections    = 12;
        public const int MaxConnections = 8;

        private UILabel     _loadingLabel;
        private UIPanel[]   _sections;
        private UIPanel[]   _headers;
        private UILabel[]   _arrows;
        private UILabel[]   _headerNames;
        private UILabel[]   _headerCounts;
        private UIPanel[]   _contents;
        private UIPanel[][] _connRows;
        private UILabel[][] _connArrows;
        private UILabel[][] _connNames;
        private UILabel[][] _connCounts;

        private int    _sectionCount    = 0;
        private int    _expandedSection = -1;
        private string _expandedName    = null;

        public override void Awake()
        {
            base.Awake();
            this.canFocus  = false;
            this.isVisible = false;
        }

        public override void Start()
        {
            base.Start();
            this.backgroundSprite            = "GenericPanel";
            this.color                       = BreakdownStyle.BgColor;
            this.autoLayout                  = true;
            this.autoLayoutDirection         = LayoutDirection.Vertical;
            this.autoFitChildrenHorizontally = true;
            this.autoFitChildrenVertically   = true;
            this.name                        = "BreakdownAccordionPanel";

            _loadingLabel = this.AddUIComponent<UILabel>();
            _loadingLabel.text       = "Loading...";
            _loadingLabel.textColor  = BreakdownStyle.MutedColor;
            _loadingLabel.textScale  = BreakdownStyle.TextScale;
            _loadingLabel.padding    = new RectOffset(8, 8, 6, 6);
            _loadingLabel.isInteractive = false;
            _loadingLabel.isVisible  = true;

            _sections     = new UIPanel[MaxSections];
            _headers      = new UIPanel[MaxSections];
            _arrows       = new UILabel[MaxSections];
            _headerNames  = new UILabel[MaxSections];
            _headerCounts = new UILabel[MaxSections];
            _contents     = new UIPanel[MaxSections];
            _connRows     = new UIPanel[MaxSections][];
            _connArrows   = new UILabel[MaxSections][];
            _connNames    = new UILabel[MaxSections][];
            _connCounts   = new UILabel[MaxSections][];

            for (int i = 0; i < MaxSections; i++)
            {
                int idx = i;

                _sections[i] = this.AddUIComponent<UIPanel>();
                _sections[i].autoLayout                  = true;
                _sections[i].autoLayoutDirection         = LayoutDirection.Vertical;
                _sections[i].autoFitChildrenHorizontally = true;
                _sections[i].autoFitChildrenVertically   = true;
                _sections[i].isVisible                   = false;

                // ── Header (clickable) ──────────────────────────────────────
                _headers[i] = _sections[i].AddUIComponent<UIPanel>();
                _headers[i].autoLayout                  = true;
                _headers[i].autoLayoutDirection         = LayoutDirection.Horizontal;
                _headers[i].autoFitChildrenHorizontally = true;
                _headers[i].autoFitChildrenVertically   = true;
                _headers[i].isInteractive               = true;
                _headers[i].canFocus                    = false;
                _headers[i].eventClick += (c, e) => ToggleSection(idx);

                _arrows[i] = _headers[i].AddUIComponent<UILabel>();
                _arrows[i].text          = "▶";
                _arrows[i].textColor     = BreakdownStyle.MutedColor;
                _arrows[i].textScale     = BreakdownStyle.TextScale;
                _arrows[i].padding       = new RectOffset(8, 4, 4, 4);
                _arrows[i].isInteractive = false;

                _headerNames[i] = _headers[i].AddUIComponent<UILabel>();
                _headerNames[i].textScale    = BreakdownStyle.TextScale;
                _headerNames[i].padding      = new RectOffset(0, 4, 4, 4);
                _headerNames[i].isInteractive = false;

                _headerCounts[i] = _headers[i].AddUIComponent<UILabel>();
                _headerCounts[i].textColor   = BreakdownStyle.MutedColor;
                _headerCounts[i].textScale   = BreakdownStyle.TextScale;
                _headerCounts[i].padding     = new RectOffset(0, 8, 4, 4);
                _headerCounts[i].isInteractive = false;

                // ── Content (collapsible) ────────────────────────────────────
                _contents[i] = _sections[i].AddUIComponent<UIPanel>();
                _contents[i].autoLayout                  = true;
                _contents[i].autoLayoutDirection         = LayoutDirection.Vertical;
                _contents[i].autoFitChildrenHorizontally = true;
                _contents[i].autoFitChildrenVertically   = true;
                _contents[i].isVisible                   = false;

                _connRows[i]   = new UIPanel[MaxConnections];
                _connArrows[i] = new UILabel[MaxConnections];
                _connNames[i]  = new UILabel[MaxConnections];
                _connCounts[i] = new UILabel[MaxConnections];

                for (int j = 0; j < MaxConnections; j++)
                {
                    _connRows[i][j] = _contents[i].AddUIComponent<UIPanel>();
                    _connRows[i][j].autoLayout                  = true;
                    _connRows[i][j].autoLayoutDirection         = LayoutDirection.Horizontal;
                    _connRows[i][j].autoFitChildrenHorizontally = true;
                    _connRows[i][j].autoFitChildrenVertically   = true;
                    _connRows[i][j].isVisible                   = false;

                    _connArrows[i][j] = _connRows[i][j].AddUIComponent<UILabel>();
                    _connArrows[i][j].text          = "↔";
                    _connArrows[i][j].textColor     = BreakdownStyle.MutedColor;
                    _connArrows[i][j].textScale     = BreakdownStyle.TextScale;
                    _connArrows[i][j].padding       = new RectOffset(20, 4, 3, 3);
                    _connArrows[i][j].isInteractive = false;

                    _connNames[i][j] = _connRows[i][j].AddUIComponent<UILabel>();
                    _connNames[i][j].textScale    = BreakdownStyle.TextScale;
                    _connNames[i][j].padding      = new RectOffset(0, 4, 3, 3);
                    _connNames[i][j].isInteractive = false;

                    _connCounts[i][j] = _connRows[i][j].AddUIComponent<UILabel>();
                    _connCounts[i][j].textColor   = BreakdownStyle.MutedColor;
                    _connCounts[i][j].textScale   = BreakdownStyle.TextScale;
                    _connCounts[i][j].padding     = new RectOffset(0, 8, 3, 3);
                    _connCounts[i][j].isInteractive = false;
                }
            }
        }

        private void ToggleSection(int index)
        {
            if (_sections == null || index >= _sectionCount) return;

            if (_expandedSection == index)
            {
                _contents[index].isVisible = false;
                _arrows[index].text        = "▶";
                _expandedSection = -1;
                _expandedName    = null;
            }
            else
            {
                if (_expandedSection >= 0 && _expandedSection < _sectionCount)
                {
                    _contents[_expandedSection].isVisible = false;
                    _arrows[_expandedSection].text        = "▶";
                }
                _expandedSection           = index;
                _expandedName              = _headerNames[index].text;
                _contents[index].isVisible = true;
                _arrows[index].text        = "▼";
            }
        }

        public void ShowLoading()
        {
            if (_loadingLabel == null) return;
            for (int i = 0; i < MaxSections; i++)
                if (_sections != null && _sections[i] != null) _sections[i].isVisible = false;
            _loadingLabel.isVisible = true;
            this.isVisible = true;
        }

        public void SetData(DistrictSection[] sections)
        {
            if (_sections == null) return;

            _sectionCount = sections != null ? Math.Min(sections.Length, MaxSections) : 0;

            // Re-locate the expanded district by name after a data refresh
            int newExpanded = -1;
            if (_expandedName != null)
                for (int i = 0; i < _sectionCount; i++)
                    if (sections[i].name == _expandedName) { newExpanded = i; break; }
            _expandedSection = newExpanded;

            for (int i = 0; i < MaxSections; i++)
            {
                if (i < _sectionCount)
                {
                    var  s        = sections[i];
                    bool expanded = i == _expandedSection;

                    _headerNames[i].text      = s.name;
                    _headerNames[i].textColor = s.color;
                    _headerCounts[i].text     = s.totalRoutes == 1 ? "(1 route)" : $"({s.totalRoutes} routes)";
                    _arrows[i].text           = expanded ? "▼" : "▶";

                    int connCount = s.connections != null ? Math.Min(s.connections.Length, MaxConnections) : 0;
                    for (int j = 0; j < MaxConnections; j++)
                    {
                        if (j < connCount)
                        {
                            var c = s.connections[j];
                            _connNames[i][j].text      = c.name;
                            _connNames[i][j].textColor = c.color;
                            _connCounts[i][j].text     = c.routes == 1 ? "(1)" : $"({c.routes})";
                            _connRows[i][j].tooltip    = c.tooltip ?? string.Empty;
                            _connRows[i][j].isVisible  = true;
                        }
                        else
                        {
                            _connRows[i][j].tooltip   = string.Empty;
                            _connRows[i][j].isVisible = false;
                        }
                    }

                    _contents[i].isVisible = expanded;
                    _sections[i].isVisible = true;
                }
                else
                {
                    _sections[i].isVisible = false;
                }
            }

            if (_loadingLabel != null) _loadingLabel.isVisible = false;
            this.isVisible = _sectionCount > 0;
        }
    }
}
