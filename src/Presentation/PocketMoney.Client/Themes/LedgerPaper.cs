using MudBlazor;

namespace PocketMoney.Client.Themes;

/// <summary>
/// "Ledger Paper" MudBlazor theme (UI Spec §1.1, ui_prototype_v03 approved).
/// One shared MD3 token set; child surfaces differentiate via the Secondary
/// (amber) accent, parent surfaces via Primary (ledger green).
/// </summary>
public static class LedgerPaper
{
    // Palette hex values (UI Spec §1.1 table)
    public const string Primary = "#14603F";
    public const string OnPrimary = "#FDFBF3";
    public const string PrimaryContainer = "#DFEDDD";
    public const string Secondary = "#B26A1B";
    public const string OnSecondary = "#FFF8EE";
    public const string SecondaryContainer = "#F7E6C8";
    public const string Tertiary = "#93493A";
    public const string TertiaryContainer = "#F6DFD3";
    public const string Background = "#F4F1E7";
    public const string Surface = "#FBF9F2";
    public const string SurfaceLow = "#F8F5EC";
    public const string SurfaceHigh = "#F1EDE0";
    public const string OnSurface = "#26312A";
    public const string OnSurfaceVariant = "#5C6B60";
    public const string Outline = "#8FA092";
    public const string OutlineVariant = "#DCE3D4";
    public const string Error = "#C23D3D";
    public const string ErrorContainer = "#FBEAEA";
    public const string Success = "#147047";
    public const string SuccessContainer = "#E7F2EC";

    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Primary,
            PrimaryContrastText = OnPrimary,
            PrimaryLighten = PrimaryContainer,
            Secondary = Secondary,
            SecondaryContrastText = OnSecondary,
            SecondaryLighten = SecondaryContainer,
            Tertiary = Tertiary,
            TertiaryLighten = TertiaryContainer,
            AppbarBackground = Surface,
            AppbarText = OnSurface,
            Background = Background,
            BackgroundGray = SurfaceLow,
            Surface = Surface,
            TextPrimary = OnSurface,
            TextSecondary = OnSurfaceVariant,
            LinesDefault = Outline,
            LinesInputs = OutlineVariant,
            Divider = OutlineVariant,
            Error = Error,
            ErrorContrastText = "#FFF6F3",
            ErrorLighten = ErrorContainer,
            Success = Success,
            SuccessContrastText = "#F0FAF4",
            SuccessLighten = SuccessContainer,
            DrawerBackground = Surface,
            DrawerText = OnSurface,
            DrawerIcon = OnSurfaceVariant,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Nunito", "Segoe UI", "system-ui", "sans-serif"],
                FontWeight = "400",
            },
            H1 = new H1Typography { FontFamily = ["Nunito"], FontWeight = "800", FontSize = "2.25rem", LineHeight = "1.2" },
            H2 = new H2Typography { FontFamily = ["Nunito"], FontWeight = "800", FontSize = "1.75rem", LineHeight = "1.25" },
            H3 = new H3Typography { FontFamily = ["Nunito"], FontWeight = "800", FontSize = "1.375rem", LineHeight = "1.3" },
            H4 = new H4Typography { FontFamily = ["Nunito"], FontWeight = "700", FontSize = "1.15rem" },
            H5 = new H5Typography { FontFamily = ["Nunito"], FontWeight = "700", FontSize = "1rem" },
            H6 = new H6Typography { FontFamily = ["Nunito"], FontWeight = "700", FontSize = ".9rem" },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Nunito"], FontWeight = "700", FontSize = "1rem" },
            Subtitle2 = new Subtitle2Typography { FontFamily = ["Nunito"], FontWeight = "600", FontSize = ".875rem" },
            Body1 = new Body1Typography { FontFamily = ["Nunito"], FontWeight = "400", FontSize = "1rem" },
            Body2 = new Body2Typography { FontFamily = ["Nunito"], FontWeight = "400", FontSize = ".875rem" },
            Button = new ButtonTypography { FontFamily = ["Nunito"], FontWeight = "700", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = ["Nunito"], FontWeight = "600", FontSize = ".75rem" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "14px",
        },
    };
}
