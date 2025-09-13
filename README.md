# Buds3Pro v1.2 · Xamarin.Android  
[![Build](https://github.com/Baal-1981/Buds3Pro-v1.2/actions/workflows/android-build.yml/badge.svg?branch=main)](https://github.com/Baal-1981/Buds3Pro-v1.2/actions/workflows/android-build.yml)
![Target SDK](https://img.shields.io/badge/targetSdk-35-blue)
![Platform](https://img.shields.io/badge/platform-Android-green)

> FR ↓ | EN ↓

---

## 🇫🇷 Présentation

**Buds3Pro v1.2** est une application **Xamarin.Android** orientée audio (latence, mixage, effets) et **Bluetooth** (gestion des écouteurs et permissions Android 12+).  
Le projet cible **Android 14/15 (targetSdk=35)** tout en restant compatible avec l’outillage Xamarin « legacy ».

### Fonctionnalités
- Routage/traitement audio (low-latency, mixage, effets).
- Gestion Bluetooth (connexion d’appareils, permissions Android 12+).
- Cible **targetSdk=35** (conforme Play Console).
- Scan BLE **optionnel** (déclaré uniquement si réellement utilisé).

### Permissions Android 12+ (API 31+)
- **BLUETOOTH_CONNECT** : demandée **à l’exécution** avant tout usage Bluetooth (API 31+).
- **BLUETOOTH_SCAN** : **uniquement si l’app scanne**. À déclarer avec `usesPermissionFlags="neverForLocation"` et, pour compatibilité ≤ Android 11, `ACCESS_FINE_LOCATION` avec `maxSdkVersion=30`.

> ⚠️ Ne versionnez jamais de secrets (keystore, mots de passe). Le `.gitignore` doit exclure `*.keystore`, `bin/`, `obj/`, `*.apk`, `*.aab`, etc.

### Construction (Build)
- **Outils** : Visual Studio 2022 + Android SDK **35** installés.
- **Xamarin.Android (legacy)** :
  - `TargetFrameworkVersion` reste `v13.0` (API 33).
  - Le manifeste cible `android:targetSdkVersion="35"`.
- Build Release local :
```bash
msbuild /restore /p:Configuration=Release
```
- CI (optionnel) : un workflow GitHub Actions peut valider un build Release (non signé) et publier les logs.

### Extrait – Demande de permission (Android 12+)
```csharp
const int ReqBtConnect = 0xB100;

void RequestBtConnectIfNeeded()
{
    if (Build.VERSION.SdkInt >= BuildVersionCodes.S &&
        CheckSelfPermission(Manifest.Permission.BluetoothConnect) != Permission.Granted)
    {
        RequestPermissions(new[] { Manifest.Permission.BluetoothConnect }, ReqBtConnect);
    }
}
```

### Roadmap
- CI Release (Actions).
- Optimisations audio/latence.
- Tests sur Samsung S23 FE (Knox).

---

## 🇬🇧 Overview

**Buds3Pro v1.2** is a **Xamarin.Android** app focused on audio (latency/effects) and **Bluetooth** (headset handling & Android 12+ runtime permissions).  
It targets **Android 14/15 (targetSdk=35)** while staying compatible with the Xamarin “legacy” toolchain.

### Features
- Audio routing/processing (low-latency, mixing, effects).
- Bluetooth device handling with Android 12+ permission flow.
- **targetSdk=35** (Play Console compliant).
- Optional BLE scan (declared only when actually used).

### Android 12+ Permissions (API 31+)
- **BLUETOOTH_CONNECT**: requested **at runtime** before any Bluetooth usage on API 31+.
- **BLUETOOTH_SCAN**: **only if the app performs scanning**. Declare with `usesPermissionFlags="neverForLocation"`. For ≤ Android 11, add `ACCESS_FINE_LOCATION` with `maxSdkVersion=30`.

> ⚠️ Never commit secrets (keystore, passwords). `.gitignore` should exclude `*.keystore`, `bin/`, `obj/`, `*.apk`, `*.aab`, etc.

### Build
- **Tooling**: Visual Studio 2022 + Android SDK **35** installed.
- **Xamarin.Android (legacy)**:
  - `TargetFrameworkVersion` stays `v13.0` (API 33).
  - Manifest targets `android:targetSdkVersion="35"`.
- Local Release build:
```bash
msbuild /restore /p:Configuration=Release
```
- CI (optional): a GitHub Actions workflow can validate a Release build (unsigned) and publish logs.

### Snippet – Runtime permission (Android 12+)
```csharp
const int ReqBtConnect = 0xB100;

void RequestBtConnectIfNeeded()
{
    if (Build.VERSION.SdkInt >= BuildVersionCodes.S &&
        CheckSelfPermission(Manifest.Permission.BluetoothConnect) != Permission.Granted)
    {
        RequestPermissions(new[] { Manifest.Permission.BluetoothConnect }, ReqBtConnect);
    }
}
```

### Roadmap
- Release CI (Actions).
- Audio/latency optimizations.
- Testing on Samsung S23 FE (Knox).

---

## 📄 License — MIT

```
MIT License

Copyright (c) 2025 Baal-1981

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 🔖 Topics / Tags
`android` · `xamarin-android` · `bluetooth` · `audio` · `hearing-aid` · `sdk-35` · `buds3pro`

---

BBBB   AAA   AAA   L               1     999     888     1
B   B  A   A A   A L              11    9   9   8   8   11
BBBB   AAAAA AAAAA L        ---    1     999     888     1
B   B  A   A A   A L               1         9   8   8    1
BBBB   A   A A   A LLLLL           1     999     888     1
