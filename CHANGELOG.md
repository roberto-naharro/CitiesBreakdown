# Changelog

## [1.0.1](https://github.com/roberto-naharro/CitiesBreakdown/compare/v1.0.0...v1.0.1) (2026-04-25)


### Bug Fixes

* update compatibility information for Cities: Skylines to version 1.21.1-f9 ([9b22f4f](https://github.com/roberto-naharro/CitiesBreakdown/commit/9b22f4f04c45e0a10fc261b79cbae748f58294a8))

## [1.0.0](https://github.com/roberto-naharro/CitiesBreakdown/compare/v0.0.1...v1.0.0) (2026-04-25)


### ⚠ BREAKING CHANGES

* initial release

### Features

* add debug logging toggle in mod options ([f531d6c](https://github.com/roberto-naharro/CitiesBreakdown/commit/f531d6cd29e83b45c0623de5b82cbcd7fc4f358c))
* add UIBreakdownAccordionPanel for enhanced UI organization and dynamic data display ([50357f0](https://github.com/roberto-naharro/CitiesBreakdown/commit/50357f01907c178c9d2a1814cab1e1cb72ebe5b2))
* centralize UI color and text scale definitions in BreakdownStyle, enhance path ranking logic for better performance ([758e799](https://github.com/roberto-naharro/CitiesBreakdown/commit/758e7995e8538256948028771c3987fd7c76a716))
* color district names using golden-ratio hue distribution ([f4b52a9](https://github.com/roberto-naharro/CitiesBreakdown/commit/f4b52a9711de383bf32e830e7c01f3e4b205d3f7))
* enhance route categorization and tooltip display for improved UI information ([a794a31](https://github.com/roberto-naharro/CitiesBreakdown/commit/a794a31e46894e7f5c2064d17a465e3d689a6ce6))
* implement EMA data handling and average mode toggle in UI components ([8040724](https://github.com/roberto-naharro/CitiesBreakdown/commit/80407241d3c8c542aafbc0a79112ed362189db24))
* initial release ([294ae69](https://github.com/roberto-naharro/CitiesBreakdown/commit/294ae694a1719cd0515e8eefa35bcf13384ede2e))
* rename mod to BreakdownRevisited and add debug logging functionality ([9719333](https://github.com/roberto-naharro/CitiesBreakdown/commit/9719333777da4f7daa4d87440c378e2062895bc1))


### Bug Fixes

* avoid capturing singletons at class-load time in CitiesExtensions ([8ab14db](https://github.com/roberto-naharro/CitiesBreakdown/commit/8ab14dbc929473465a519f1da700babc00906d28))
* catch per-panel exceptions in InitUI, use m_size for More PathUnits compat ([e870448](https://github.com/roberto-naharro/CitiesBreakdown/commit/e87044897ac279017000de67581ac92c7c64e14d))
* CS0472 warning, panel NRE, dead code cleanup ([e9f6d97](https://github.com/roberto-naharro/CitiesBreakdown/commit/e9f6d97e85b4c89306de18d01bad6e1f872be318))
* enable CitizenWorldInfoPanel for improved UI information display ([28f7088](https://github.com/roberto-naharro/CitiesBreakdown/commit/28f7088956aa3858af9846c38c31fcef7bea9811))
* enhance district color handling and improve nearest district resolution logic ([ff427a5](https://github.com/roberto-naharro/CitiesBreakdown/commit/ff427a5ec54999aaa7c06925e4c1021f8e0dc63b))
* enhance district position handling and improve nearest district resolution logic ([6ca7cb4](https://github.com/roberto-naharro/CitiesBreakdown/commit/6ca7cb4a1633840f43f0bbbb3124868f80d3a511))
* enhance logging for route processing and improve UI panel initialization ([f7d10bd](https://github.com/roberto-naharro/CitiesBreakdown/commit/f7d10bd8df7ff354ed8b4f3a738945d0922cd3ef))
* enhance route data handling and improve UI panel visibility ([17b58c6](https://github.com/roberto-naharro/CitiesBreakdown/commit/17b58c6dd08446a3d6f6be26c31852ed30d04597))
* enhance route processing with pending updates and improve UI responsiveness ([16a9127](https://github.com/roberto-naharro/CitiesBreakdown/commit/16a912797a567a1788d1206744f866cbad6be5bd))
* enhance SetTopTen method to support from/to labels and improve UI panel visibility ([a93e26a](https://github.com/roberto-naharro/CitiesBreakdown/commit/a93e26a28d7d1543cf23236cc1e2de949817ad4f))
* harden m_paths reflection with 'as' cast and try/catch for race condition ([aff59f4](https://github.com/roberto-naharro/CitiesBreakdown/commit/aff59f42cbf95770995abe091851ec892782db15))
* improve path ranking logic and update UI buttons for better interaction ([f12687a](https://github.com/roberto-naharro/CitiesBreakdown/commit/f12687a051a76edd422de8d82f4c0580cf425d8e))
* improve route processing and UI responsiveness with updated coroutine handling ([a40488f](https://github.com/roberto-naharro/CitiesBreakdown/commit/a40488f67c1578450752cd1e103542604523e1ac))
* increase row count in UI and improve distance calculation logic ([05eb6dc](https://github.com/roberto-naharro/CitiesBreakdown/commit/05eb6dcf735435aded2ff7bff46f64ab98c27681))
* rank routes by path count instead of broken TotalReferences metric ([7d1188d](https://github.com/roberto-naharro/CitiesBreakdown/commit/7d1188db69cc588386dfc60a6f95ae4455123545))
* remove path count change detection causing constant rescanning ([1b3dc06](https://github.com/roberto-naharro/CitiesBreakdown/commit/1b3dc06b851cdf3457d2ae60adc47ff1ec660f73))
* trigger immediate scan when route visualizer becomes visible ([d508eb2](https://github.com/roberto-naharro/CitiesBreakdown/commit/d508eb29f94dd9b7e32e9d27d6f5bfd2f465610c))
* update SetTopTen method to include prefixes and improve UI panel display logic ([b1f99bf](https://github.com/roberto-naharro/CitiesBreakdown/commit/b1f99bfd3d7cdc0381f08fb99e9a7ac2517dcaa7))
* update SetTopTen method to include tags and improve UI visibility for route data ([7a7b888](https://github.com/roberto-naharro/CitiesBreakdown/commit/7a7b888686f1a5b408b8169dfbba00fe127e9993))
* update UIBreakdownPanel to support dynamic district and road toggling, enhance row count and improve UI responsiveness ([854741a](https://github.com/roberto-naharro/CitiesBreakdown/commit/854741adfc2ae59a93fd6094916c32006a135387))
* update visibility logic for UI panels based on names array length ([80b80c2](https://github.com/roberto-naharro/CitiesBreakdown/commit/80b80c296aa5853d56e55e80ce51fd7222f4a829))

## [1.0.0] - 2026-04-25

### Features

- Route popup panel on every WorldInfoPanel — top-25 origin→destination district pairs ranked by traffic volume
- City-wide accordion panel in Traffic Routes mode — districts ranked by total connections with per-connection detail
- Route type tooltip breakdown: Pedestrians, Cyclists, Private Cars, Trucks, Public Transit, City Services
- Districts / Roads toggle to switch between district-level and road-segment-level display
- Average mode — exponential moving average (EMA) of traffic shares accumulated over the session; shows smoothed historical patterns with percentage display and colour-coded alerts
- Colour-coded district labels for quick visual scanning
- Pass-through route detection: same-tag indicator when origin and destination share a district boundary
