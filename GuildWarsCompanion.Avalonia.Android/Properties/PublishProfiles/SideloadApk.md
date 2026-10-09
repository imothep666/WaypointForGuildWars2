# Sideload APK

Produces one signed `.apk` to install straight onto a phone. Android 15 (API 35) is well
inside the range: the package declares `minSdkVersion 24` and `targetSdkVersion 36`.

## Build it

From the repository root:

```
dotnet publish GuildWarsCompanion.Avalonia.Android/GuildWarsCompanion.Avalonia.Android.csproj -p:PublishProfile=SideloadApk
```

or `.\publish-apk.ps1`, which is the same command with the arguments filled in.

The result lands at:

```
GuildWarsCompanion.Avalonia.Android/bin/Release/net10.0-android/android-arm64/publish/
    com.mpsoft.gw2companion-Signed.apk      <- install this one
    com.mpsoft.gw2companion.apk             <- unsigned, ignore
```

Take the **`-Signed`** file. The other one will not install.

## Put it on the phone

Over USB with debugging enabled:

```
adb install -r com.mpsoft.gw2companion-Signed.apk
```

Or copy the file to the device and open it, then allow "install unknown apps" for whichever
app you opened it from (Files, Drive, your browser). Android 15 asks per source app.

If an install fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, a build signed by a different
key is already installed — uninstall the old one first.

## About the signature

Signed with the app's own key, not the SDK's debug key. Android installs an update only if it
carries the signature the app was installed with, and the debug key is made up separately on
every machine, so a debug-signed build from a second PC would not update the phone; only an
uninstall would, and that loses the API key, the pairings, the widgets and the caches.

The key lives outside the repository, in `<user profile>\.android\gw2companion\`:

```
release.keystore    PKCS12, alias gw2companion, RSA 4096, CN=mpsoft, valid until 2054
release.password    its password, on two lines, the same both times
```

Two lines because apksigner reads one line per password from a shared file, the store's and
then the key's. A PKCS12 key has no password of its own, so both are the same; with only one
line the signing step fails with "end of file reached".

The project file signs every build with it when it is there, Debug included, so a debug install
and a sideload update each other. A **Release build without it fails** with a message naming
the path, rather than producing an APK the phone refuses.

**Neither file may be committed**, and both are needed: the keystore is useless without the
password. Back them up together (a password manager takes both), because a lost key means the
app can never be updated under this identity again; only an uninstall moves to a new one.

### Another PC

Copy both files into the same folder there, `<user profile>\.android\gw2companion\`. Nothing
else changes: the same `.\publish-apk.ps1`, the same signature, and its builds update the phone
in place. A PC without the files still builds Debug, signed with its own debug key; installing
that over this app fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, which is the sign to copy
the key over.

### Checking an APK

```
apksigner verify --print-certs com.mpsoft.gw2companion-Signed.apk
```

should name `CN=mpsoft, O=mpsoft` with SHA-256
`d3859e06843f4f79bfca2a85f01fae447a39150953b3529feda8943b86ef0340`. `apksigner` is in the
Android SDK's `build-tools`.

### For the Play Store

Play takes `.aab` (`<AndroidPackageFormat>aab</AndroidPackageFormat>` in a second profile) and
re-signs with its own key under Play App Signing; this key would then be the upload key.

## Architecture

The profile builds `android-arm64` only, which every Android 15 phone is. Two consequences:

- It will **not** install on the standard x86_64 emulator. For that, publish with
  `-p:RuntimeIdentifier=android-x64`.
- To make one package that runs on both, change the profile to
  `<RuntimeIdentifiers>android-arm64;android-x64</RuntimeIdentifiers>` (plural). It roughly
  doubles the size — currently about 36 MB per architecture.
