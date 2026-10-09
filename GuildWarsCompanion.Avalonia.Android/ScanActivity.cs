using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;

using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;

using GuildWarsCompanion.Ava.Host;

using Java.Util.Concurrent;

using System.Runtime.InteropServices;

using ZXing;

using Color = Android.Graphics.Color;

namespace GuildWarsCompanion.Ava.Droid;

/// <summary>
/// The phone's scanner: Android's own camera through CameraX, with ZXing reading each frame.
///
/// A native activity rather than an Avalonia page with a camera view inside it. Avalonia has no
/// camera control, and CameraX wants a lifecycle owner and a native view of its own; a separate
/// activity has both, and Android's back gesture leaves it the way it leaves any other.
/// </summary>
public sealed class CameraQrScanner : IQrScanner
{
    public bool UsesCamera => true;

    public Task<string?> ScanAsync(ScanRequest request)
    {
        if (MainActivity.Current is not { } activity) return Task.FromResult<string?>(null);

        // One scan at a time: a second tap while the camera is up gets the same answer.
        if (ScanActivity.Pending is { Task.IsCompleted: false } running) return running.Task;

        ScanActivity.Request = request;
        ScanActivity.Pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        activity.StartActivity(new Intent(activity, typeof(ScanActivity)));
        return ScanActivity.Pending.Task;
    }
}

[Activity(Theme = "@style/CompanionTheme", ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class ScanActivity : AppCompatActivity
{
    private const int CameraRequest = 7;

    internal static ScanRequest? Request;
    internal static TaskCompletionSource<string?>? Pending;

    private PreviewView? _preview;
    private TextView? _status;
    private View? _reticle;
    private int _reticleSide;
    private IExecutorService? _decoding;
    private ProcessCameraProvider? _provider;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        SetContentView(Layout());

        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
            StartCamera();
        else
            RequestPermissions([Manifest.Permission.Camera], CameraRequest);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != CameraRequest) return;

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            StartCamera();
            return;
        }

        // Without the camera there is nothing to show: say so instead of a black rectangle.
        _reticle!.Visibility = ViewStates.Gone;
        _status!.Text = "Scanning needs camera access. Grant it in Android's app settings, " +
                        "or paste the key by hand under Settings.";
    }

    protected override void OnDestroy()
    {
        _provider?.UnbindAll();
        _decoding?.Shutdown();

        // Backed out without a code: the scan answers null rather than never.
        Pending?.TrySetResult(null);

        base.OnDestroy();
    }

    private void StartCamera()
    {
        var future = ProcessCameraProvider.GetInstance(this);

        future.AddListener(new Java.Lang.Runnable(() =>
        {
            _provider = (ProcessCameraProvider)future.Get()!;

            var preview = new Preview.Builder().Build()!;
            preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(this), _preview!.SurfaceProvider);

            // CameraX analyses at 640x480 unless asked otherwise. That reads an API key, but the
            // bridge's pairing card is a denser code shown on a monitor, and at that size each
            // module is two or three pixels of moiré. Frames as large as the preview's read it.
            var resolution = new AndroidX.Camera.Core.ResolutionSelector.ResolutionSelector.Builder()
                .SetResolutionStrategy(new AndroidX.Camera.Core.ResolutionSelector.ResolutionStrategy(
                    new Size(1600, 1200),
                    AndroidX.Camera.Core.ResolutionSelector.ResolutionStrategy.FallbackRuleClosestLowerThenHigher))!
                .Build();

            var analysis = new ImageAnalysis.Builder()
                .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)!
                .SetResolutionSelector(resolution)!
                .Build()!;

            _decoding = Executors.NewSingleThreadExecutor()!;
            analysis.SetAnalyzer(_decoding, new Analyzer(this));

            _provider.UnbindAll();
            _provider.BindToLifecycle(this, CameraSelector.DefaultBackCamera!, preview, analysis);
        }), ContextCompat.GetMainExecutor(this));
    }

    /// <summary>Called off the main thread for every frame the decoder can keep up with.</summary>
    private void OnDecoded(string payload)
    {
        if (Request is not { } request || Pending is not { } pending || pending.Task.IsCompleted) return;

        if (!request.Accept(payload))
        {
            // Keep scanning — the user probably framed the wrong code.
            RunOnUiThread(() => _status!.Text = request.Rejected);
            return;
        }

        pending.TrySetResult(payload);
        RunOnUiThread(Finish);
    }

    /// <summary>
    /// The screen in code: the preview filling it, a brass square framing the shot, and one line
    /// of instruction underneath.
    /// </summary>
    private View Layout()
    {
        var obsidian = Color.ParseColor("#0F141B");
        var root = new FrameLayout(this);
        root.SetBackgroundColor(obsidian);

        _preview = new PreviewView(this);
        root.AddView(_preview, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        // The reticle is also where decoding looks; see Window.
        var frame = new GradientDrawable();
        frame.SetStroke(Dp(2), Color.ParseColor("#C8A34E"));
        frame.SetCornerRadius(Dp(2));

        _reticle = new View(this) { Background = frame };
        _reticleSide = Dp(240);
        root.AddView(_reticle, new FrameLayout.LayoutParams(_reticleSide, _reticleSide, GravityFlags.Center));

        _status = new TextView(this)
        {
            Text = Request?.Prompt ?? string.Empty,
            Gravity = GravityFlags.Center
        };
        _status.SetTextColor(Color.ParseColor("#E6DECB"));
        _status.SetTextSize(ComplexUnitType.Sp, 14);
        _status.SetBackgroundColor(Color.Argb(0xCC, 0x0F, 0x14, 0x1B));
        _status.SetPadding(Dp(20), Dp(16), Dp(20), Dp(28));

        root.AddView(_status, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));

        return root;
    }

    private int Dp(float value) =>
        (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, value, Resources!.DisplayMetrics);

    /// <summary>
    /// The square of a frame under the reticle, half as wide again for an unsteady hand, in the
    /// frame's own pixels. Null until the preview has been laid out.
    ///
    /// The frame arrives in the sensor's orientation, but a centred square stays a centred square
    /// whichever way it is turned, so only the scale needs the rotation. PreviewView fills its
    /// view and crops what overflows, which makes the scale the larger of the two ratios.
    /// </summary>
    private (int Left, int Top, int Side)? UnderReticle(int width, int height, int rotation)
    {
        if (_preview is not { Width: > 0, Height: > 0 } view) return null;

        var (uprightWidth, uprightHeight) = rotation % 180 == 0 ? (width, height) : (height, width);
        var scale = Math.Max((double)view.Width / uprightWidth, (double)view.Height / uprightHeight);
        var side = (int)Math.Min(Math.Min(width, height), _reticleSide * 1.5 / scale);

        return ((width - side) / 2, (height - side) / 2, side);
    }

    /// <summary>
    /// Reads the brightness plane of each frame. A QR code is black and white, so the Y plane is
    /// all ZXing needs.
    ///
    /// Most frames are read only under the reticle, where the code is framed anyway: about a
    /// sixth of the pixels, and a frame that holds no code costs a sixth of the time. Every
    /// fourth frame is read whole at half size instead, for a code held closer than the reticle
    /// — one that close is large enough to survive the halving, and a whole frame at full size
    /// took over two seconds to give up on.
    /// </summary>
    private sealed class Analyzer(ScanActivity owner) : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        private byte[] _row = [];
        private byte[] _luma = [];
        private int _frames;

        public void Analyze(IImageProxy? image)
        {
            if (image is null) return;

            try
            {
                var plane = image.GetPlanes()![0];
                var width = image.Width;
                var height = image.Height;
                var stride = plane.RowStride;

                // CameraX hands out direct buffers, so the pixels can be copied straight out of
                // native memory rather than a row at a time through JNI.
                var address = JNIEnv.GetDirectBufferAddress(plane.Buffer!.Handle);
                if (address == IntPtr.Zero) return;

                var reticle = _frames++ % 4 == 0
                    ? null
                    : owner.UnderReticle(width, height, image.ImageInfo!.RotationDegrees);

                var (outWidth, outHeight) = reticle is { } square
                    ? (square.Side, square.Side)
                    : (width / 2, height / 2);

                if (_luma.Length != outWidth * outHeight) _luma = new byte[outWidth * outHeight];

                // Rows can be padded past the image's width; keep only the pixels.
                if (reticle is { } r)
                {
                    for (var y = 0; y < outHeight; y++)
                        Marshal.Copy(address + (r.Top + y) * stride + r.Left, _luma, y * outWidth, outWidth);
                }
                else
                {
                    if (_row.Length != width) _row = new byte[width];

                    for (var y = 0; y < outHeight; y++)
                    {
                        Marshal.Copy(address + y * 2 * stride, _row, 0, width);
                        for (var x = 0; x < outWidth; x++) _luma[y * outWidth + x] = _row[x * 2];
                    }
                }

                var source = new PlanarYUVLuminanceSource(_luma, outWidth, outHeight, 0, 0, outWidth, outHeight, false);
                var payload = Codes.Read(source);

                if (payload is { Length: > 0 }) owner.OnDecoded(payload);
            }
            catch (Exception)
            {
                // A frame that cannot be read is just the next frame's job.
            }
            finally
            {
                image.Close();
            }
        }
    }
}
