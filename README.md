[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build](https://github.com/Baal-1981/Buds3Pro-v1.2/actions/workflows/android-build.yml/badge.svg?branch=main)](https://github.com/Baal-1981/Buds3Pro-v1.2/actions/workflows/android-build.yml)


# Buds3Pro v1.2

App Android (Xamarin.Android) pour routage audio et Bluetooth (API 35).

## Fonctionnalités
- Gestion audio (latence basse, mixage…)
- Bluetooth (CONNECT 31+, SCAN **optionnel**)
- Cible Android 14/15 (targetSdk=35)

## Permissions Android 12+
- `BLUETOOTH_CONNECT` demandée à l’exécution (API 31+)
- `BLUETOOTH_SCAN` **uniquement si l’app scanne** (avec `neverForLocation` + `ACCESS_FINE_LOCATION` maxSdk=30 pour compatibilité ≤ Android 11)

## Build (Xamarin.Android)
- Visual Studio 2022 + SDK Android 35 installés
- Build Release : `msbuild /restore /p:Configuration=Release`

## Roadmap
- [ ] CI Release (Actions)
- [ ] Optimisations latence audio
- [ ] Tests sur S23 FE (Knox)

## Licence
MIT (au choix)
