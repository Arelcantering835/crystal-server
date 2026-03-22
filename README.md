# crystal-server

Command and control server for CrystalC2.

## Authentication

This uses `appsettings.json` to control authentication.  Before use, set the JWT key to something random, and set the password that clients will use to connect.

```json
  "Jwt": {
    "Key": "CHANGE-THIS-TO-A-SECURE-RANDOM-KEY-AT-LEAST-32-CHARS"
  },
  "Auth": {
    "Password": "changeme"
  }
```