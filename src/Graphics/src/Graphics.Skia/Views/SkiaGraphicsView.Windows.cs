using SkiaSharp.Views.Windows;

namespace Microsoft.Maui.Graphics.Skia.Views
{
	/// <summary>
	/// A SkiaSharp-based graphics view for Windows that can render <see cref="IDrawable"/> objects.
	/// </summary>
	public partial class SkiaGraphicsView : SKXamlCanvas
	{
		private IDrawable _drawable;
		private SkiaCanvas _canvas;
		private ScalingCanvas _scalingCanvas;

		/// <summary>
		/// Initializes a new instance of the <see cref="SkiaGraphicsView"/> class.
		/// </summary>
		/// <param name="drawable">The drawable object to render in this view.</param>
		public SkiaGraphicsView(IDrawable drawable = null)
		{
			_canvas = new SkiaCanvas();
			_scalingCanvas = new ScalingCanvas(_canvas);
			Drawable = drawable;
		}

		/// <summary>
		/// Gets or sets the drawable object to render in this view.
		/// </summary>
		public IDrawable Drawable
		{
			get => _drawable;
			set
			{
				_drawable = value;
				Invalidate();
			}
		}

		protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
		{
			var skiaCanvas = e.Surface.Canvas;
			skiaCanvas.Clear();

			if (_drawable == null)
				return;

			var scale = (float)Dpi;

			_canvas.Canvas = skiaCanvas;

			_scalingCanvas.ResetState();
			_scalingCanvas.Scale(scale, scale);

			// The base canvas can paint from SizeChanged before a derived SizeChanged handler runs.
			_drawable.Draw(_scalingCanvas, new RectF(0, 0, (float)ActualWidth, (float)ActualHeight));
		}
	}
}
