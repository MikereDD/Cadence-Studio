# SDK compatibility

Cadence Studio v1.0-dev.10.9 targets .NET 8 for Windows and WPF.

Validated target configuration:

```text
SDK family: 8.0.x
App target: net8.0-windows
Core target: net8.0
```

The user's installed .NET 8.0.422 SDK is supported. `global.json` uses roll-forward
within the .NET 8 SDK family rather than requiring one exact patch release.
