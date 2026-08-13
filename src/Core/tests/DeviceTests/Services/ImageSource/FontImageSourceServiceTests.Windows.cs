#if UNO
using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Xunit;

namespace Microsoft.Maui.DeviceTests
{
	public partial class FontImageSourceServiceTests
	{
		[Fact]
		[Category(TestCategory.Fonts)]
		public Task GetFontCandidates_PreservesFallbackCandidatesAndActualFamilyNames() =>
			InvokeOnMainThreadAsync(() =>
			{
				var registrar = new FontRegistrar(fontLoader: null);
				registrar.Register("dokdo_regular.ttf", "dokdo");
				var manager = new FontManager(registrar);
				var actualSource = manager.GetFontFamily(Font.OfSize("dokdo", 12, FontWeight.Regular)).Source;
				var sourceWithWrongFragment = actualSource.Replace("#Dokdo", "#NotDokdo", StringComparison.OrdinalIgnoreCase);

				var service = new FontImageSourceService(new StubFontManager($"Missing Family, {sourceWithWrongFragment}, Arial"));

				var candidates = service.GetFontCandidates(Font.Default);

				Assert.Collection(candidates,
					candidate =>
					{
						Assert.Equal("Missing Family", candidate.FamilyName);
						Assert.Null(candidate.FilePath);
					},
					candidate =>
					{
						Assert.Equal("Dokdo", candidate.FamilyName);
						Assert.NotNull(candidate.FilePath);
					},
					candidate =>
					{
						Assert.Equal("Arial", candidate.FamilyName);
						Assert.Null(candidate.FilePath);
					});
			});

		[Fact]
		[Category(TestCategory.Fonts)]
		public Task ResolveTypeface_PrefersExactFileBeforeFamily() =>
			InvokeOnMainThreadAsync(() =>
			{
				var registrar = new FontRegistrar(fontLoader: null);
				registrar.Register("dokdo_regular.ttf", "dokdo");
				var manager = new FontManager(registrar);
				var actualSource = manager.GetFontFamily(Font.OfSize("dokdo", 12, FontWeight.Regular)).Source;
				var sourceWithWrongFamily = actualSource.Replace("#Dokdo", "#Arial", StringComparison.OrdinalIgnoreCase);

				var service = new FontImageSourceService(new StubFontManager(sourceWithWrongFamily));
				using var typeface = InvokeResolveTypeface(service, Font.OfSize("dokdo", 12, FontWeight.Bold));

				Assert.Equal("Dokdo", GetFamilyName(typeface));
			});

		[Fact]
		[Category(TestCategory.Fonts)]
		public Task ResolveTypeface_PrefersFileBackedCandidatesBeforeEarlierBareFamilies() =>
			InvokeOnMainThreadAsync(() =>
			{
				var registrar = new FontRegistrar(fontLoader: null);
				registrar.Register("dokdo_regular.ttf", "dokdo");
				registrar.Register("ionicons.ttf", "ionicons");
				var manager = new FontManager(registrar);
				var dokdoSource = manager.GetFontFamily(Font.OfSize("dokdo", 12, FontWeight.Regular)).Source;
				var ioniconsSource = manager.GetFontFamily(Font.OfSize("ionicons", 12, FontWeight.Regular)).Source;

				var service = new FontImageSourceService(new StubFontManager($"Arial, {dokdoSource}, {ioniconsSource}"));
				using var typeface = InvokeResolveTypeface(service, Font.OfSize("dokdo", 12, FontWeight.Bold));

				Assert.Equal("Dokdo", GetFamilyName(typeface));
			});

		static IDisposable InvokeResolveTypeface(FontImageSourceService service, Font font) =>
			(IDisposable)typeof(FontImageSourceService)
				.GetMethod("ResolveTypeface", BindingFlags.Instance | BindingFlags.NonPublic)!
				.Invoke(service, new object[] { font })!;

		static string? GetFamilyName(IDisposable typeface) =>
			(string?)typeface
				.GetType()
				.GetProperty("FamilyName", BindingFlags.Instance | BindingFlags.Public)?
				.GetValue(typeface);

		sealed class StubFontManager : IFontManager
		{
			readonly FontFamily _fontFamily;

			public StubFontManager(string source) =>
				_fontFamily = new FontFamily(source);

			public FontFamily DefaultFontFamily => _fontFamily;

			public double DefaultFontSize => 12;

			public FontFamily GetFontFamily(Font font) => _fontFamily;

			public double GetFontSize(Font font, double defaultFontSize = 0) =>
				defaultFontSize > 0 ? defaultFontSize : DefaultFontSize;
		}
	}
}
#endif
