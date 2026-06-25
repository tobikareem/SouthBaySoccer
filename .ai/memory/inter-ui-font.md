---
name: inter-ui-font
description: MAUI product UI uses Inter from Google Fonts
created: 2026-06-25
type: convention
---

The MAUI product UI uses Inter as the brand app font because `documentation/mobile-wireframes.html` specifies Inter and the font is available through Google Fonts. The app registers `Inter-Regular.ttf`, `Inter-SemiBold.ttf`, and `Inter-Bold.ttf` in `MauiProgram.cs`; shared brand typography in `BrandStyles.xaml` should use `InterRegular` and `InterSemibold`.

Keep OpenSans resources only for legacy/sample compatibility unless those screens are intentionally migrated.

Related: [[client-reusable-ui]], [[mobile-wireframes-design-source]], [[brand-green-white]]