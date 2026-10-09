using System.Globalization;
using System.Text;

using Android.Content;
using Android.Hardware;
using Android.Runtime;
using Android.Security.Keystore;
using Android.Views;

using GuildWarsCompanion.Ava.Host;
using GuildWarsCompanion.Host;

using Java.Security;

using Javax.Crypto;
using Javax.Crypto.Spec;

namespace GuildWarsCompanion.Ava.Droid;

/// <summary>
/// Settings in SharedPreferences, the store MAUI's Preferences wraps on Android.
///
/// Doubles and dates have no SharedPreferences type of their own and are kept as invariant text,
/// so a file read by eye says what it holds.
/// </summary>
public sealed class PreferenceSettings(Context context) : ISettings
{
    private readonly ISharedPreferences _store =
        context.GetSharedPreferences("settings", FileCreationMode.Private)!;

    public T Get<T>(string key, T defaultValue)
    {
        if (!_store.Contains(key)) return defaultValue;

        try
        {
            object? value = defaultValue switch
            {
                string text => _store.GetString(key, text),
                bool flag => _store.GetBoolean(key, flag),
                int number => _store.GetInt(key, number),
                long number => _store.GetLong(key, number),
                float number => _store.GetFloat(key, number),
                double number => double.TryParse(_store.GetString(key, null), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed) ? parsed : number,
                DateTime time => DateTime.TryParse(_store.GetString(key, null), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed) ? parsed : time,
                _ when typeof(T) == typeof(string) => _store.GetString(key, null),
                _ => defaultValue
            };

            return value is T typed ? typed : defaultValue;
        }
        catch (Java.Lang.ClassCastException)
        {
            // Stored as another type by an older build. The default is the honest answer.
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        using var editor = _store.Edit()!;

        switch (value)
        {
            case null: editor.Remove(key); break;
            case string text: editor.PutString(key, text); break;
            case bool flag: editor.PutBoolean(key, flag); break;
            case int number: editor.PutInt(key, number); break;
            case long number: editor.PutLong(key, number); break;
            case float number: editor.PutFloat(key, number); break;
            case double number: editor.PutString(key, number.ToString("R", CultureInfo.InvariantCulture)); break;
            case DateTime time: editor.PutString(key, time.ToString("O", CultureInfo.InvariantCulture)); break;
            default: throw new NotSupportedException($"Settings cannot hold a {typeof(T).Name}.");
        }

        editor.Apply();
    }

    public void Remove(string key)
    {
        using var editor = _store.Edit()!;
        editor.Remove(key);
        editor.Apply();
    }
}

/// <summary>
/// Secrets sealed with a key that lives in the Android Keystore and never leaves it.
///
/// AES-GCM, one fresh IV per value, stored beside the ciphertext in a preferences file of its
/// own. What is on disk is useless without the Keystore key, which is bound to this app on this
/// device — the same guarantee MAUI's SecureStorage gave.
/// </summary>
public sealed class KeystoreSecrets(Context context) : ISecrets
{
    private const string Alias = "companion_secrets";
    private const string Provider = "AndroidKeyStore";

    private readonly ISharedPreferences _store =
        context.GetSharedPreferences("secrets", FileCreationMode.Private)!;

    private readonly Lock _gate = new();

    public Task<string?> GetAsync(string key)
    {
        lock (_gate)
        {
            if (_store.GetString(key, null) is not { } stored) return Task.FromResult<string?>(null);

            try
            {
                var parts = stored.Split(':');
                var iv = Convert.FromBase64String(parts[0]);
                var sealedBytes = Convert.FromBase64String(parts[1]);

                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, iv));

                return Task.FromResult<string?>(Encoding.UTF8.GetString(cipher.DoFinal(sealedBytes)!));
            }
            catch (Exception ex) when (ex is Java.Lang.Exception or FormatException or IndexOutOfRangeException)
            {
                // A key the Keystore no longer has — restored from a backup, or reset. Treated as
                // absent: the cost is entering the API key again.
                return Task.FromResult<string?>(null);
            }
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_gate)
        {
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, Key());

            var sealedBytes = cipher.DoFinal(Encoding.UTF8.GetBytes(value))!;
            var stored = $"{Convert.ToBase64String(cipher.GetIV()!)}:{Convert.ToBase64String(sealedBytes)}";

            using var editor = _store.Edit()!;
            editor.PutString(key, stored);
            editor.Apply();
        }

        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (!_store.Contains(key)) return false;

            using var editor = _store.Edit()!;
            editor.Remove(key);
            editor.Apply();
            return true;
        }
    }

    private static IKey Key()
    {
        var store = KeyStore.GetInstance(Provider)!;
        store.Load(null);

        if (store.GetKey(Alias, null) is { } existing) return existing;

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Provider)!;
        generator.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .Build());

        return generator.GenerateKey()!;
    }
}

/// <summary>
/// Gravity off Android's accelerometer, in g, the unit MAUI's Accelerometer reports and
/// <see cref="Services.DeviceTilt"/> was tuned against.
///
/// One of these per tilt: each registers and unregisters its own listener.
/// </summary>
public sealed class AndroidAccelerometer(Context context) : Java.Lang.Object, IAccelerometer, ISensorEventListener
{
    private readonly SensorManager? _sensors = context.GetSystemService(Context.SensorService) as SensorManager;
    private Action<double, double>? _reading;

    public bool IsSupported => _sensors?.GetDefaultSensor(SensorType.Accelerometer) is not null;

    public void Start(Action<double, double> reading)
    {
        _reading = reading;

        if (_sensors?.GetDefaultSensor(SensorType.Accelerometer) is not { } sensor ||
            !_sensors.RegisterListener(this, sensor, SensorDelay.Ui))
            throw new InvalidOperationException("The accelerometer would not start.");
    }

    public void Stop() => _sensors?.UnregisterListener(this);

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values is { Count: >= 2 } values)
            _reading?.Invoke(values[0] / SensorManager.GravityEarth, values[1] / SensorManager.GravityEarth);
    }

    public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy)
    {
    }
}

/// <summary>The window's own keep-screen-on flag, on whichever activity is showing the app.</summary>
public sealed class ActivityKeepAwake : IKeepAwake
{
    private bool _on;

    public bool KeepScreenOn
    {
        get => _on;
        set
        {
            _on = value;

            if (MainActivity.Current?.Window is not { } window) return;

            if (value)
                window.AddFlags(WindowManagerFlags.KeepScreenOn);
            else
                window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        }
    }
}
