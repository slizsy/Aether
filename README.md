# Aether

Fast username availability checker for Discord, for Windows and macOS.

![Windows](https://img.shields.io/badge/Windows-0078D6?style=flat-square)
![macOS](https://img.shields.io/badge/macOS-000000?style=flat-square)
![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)

<div align="center">

<img src="assets/menu.png" alt="Aether main menu" width="760">

</div>

## Download

| System | File |
|---|---|
| Windows 10 / 11 | [Aether-windows.exe](../../releases/latest/download/Aether-windows.exe) |
| Mac with Apple Silicon (M1 and newer) | [Aether-mac-apple-silicon.zip](../../releases/latest/download/Aether-mac-apple-silicon.zip) |
| Mac with Intel | [Aether-mac-intel.zip](../../releases/latest/download/Aether-mac-intel.zip) |

All builds are on the [Releases](../../releases/latest) page. Nothing else to install.

**Windows:** run `Aether-windows.exe`. If SmartScreen shows up, click **More info**, then **Run anyway**.

**macOS:** unzip, then double-click `Aether`. macOS blocks apps from unknown developers the first time, so either:

- open **System Settings > Privacy & Security**, scroll down and click **Open Anyway**, or
- run this once in Terminal from the folder you unzipped to:

  ```
  xattr -d com.apple.quarantine Aether
  ```

Aether keeps its files in folders next to the app, so put it in its own folder.

## Features

- 9 username patterns, from plain letters to palindromes
- Bring your own list, or paste usernames straight into the app
- Local validation: 2-32 characters, lowercase letters, digits, `_` and `.`, no duplicates
- Names you already checked are skipped next time
- Proxy support for HTTP, HTTPS and SOCKS5, with login
- Paste proxies from the menu, test them, and remove the dead ones
- Bad or rate-limited proxies rest automatically and are skipped
- Connection check before every run, so a broken setup fails fast
- Live progress bar with speed and ETA
- Settings presets: auto, safe, balanced, fast, custom
- Discord webhook and optional beep when a name is available
- Per-session results and logs, plus all-time stats

## Usage

| Option | What it does |
|---|---|
| 1. Start checking | Runs a connection check, then checks every ready username |
| 2. Generate usernames | Pick a pattern, a length and how many names to make |
| 3. Paste usernames | Check your own list instead |
| 4. Proxies | Paste, test, edit or remove proxies |
| 5. Settings | Presets, threads, timeout, retries, webhook, beep |
| 6. Stats and files | All-time stats, output folders, forget checked names |

Press `Ctrl+C` during a run to stop it cleanly.

### Patterns

<div align="center">

<img src="assets/patterns.png" alt="Pattern selection" width="760">

</div>

| # | Pattern | Examples |
|---|---|---|
| 1 | Letters Only | `abc`, `xyz` |
| 2 | Alphanumeric | `a1b2`, `xyz9` |
| 3 | Double Letters | `vvss`, `ddff` |
| 4 | Period Dot Mix | `a.b`, `x.yz` |
| 5 | Triple + Different | `aaae`, `bbbx` |
| 6 | Underscore Mix | `a_b_c`, `x_y` |
| 7 | Vowel-Heavy | `baki`, `zito` |
| 8 | Palindrome | `abccba`, `xyzzyx` |
| 9 | Random Mix | a bit of everything |

### Proxies

One proxy per line in `data/proxies.txt`, or paste them under **Proxies**. All of these work:

```
host:port
host:port:username:password
http://username:password@host:port
socks5://username:password@host:port
```

See [`examples/`](examples) for sample files.

## Configuration

Settings are stored in `data/config.json` and can be changed from the app.

| Key | Default | Description |
|---|---|---|
| `usernames.custom` | `false` | Use `data/usernames.txt` instead of generating names |
| `usernames.pattern` | `mix` | `letters`, `alphanumeric`, `double`, `dot`, `triple`, `underscore`, `vowel`, `palindrome` or `mix` |
| `usernames.length` | `3` | Length of generated names |
| `usernames.amount` | `1000` | How many names to generate |
| `retry.enabled` | `true` | Retry failed requests |
| `retry.max_attempts` | `5` | Attempts per username |
| `threads` | `20` | Concurrent checks |
| `timeout` | `15` | Request timeout in seconds |
| `webhook` | `""` | Discord webhook URL for available names |
| `verbose` | `true` | Show every result, not only available names |
| `beep` | `false` | Beep when a name is available |

## Output

```
data/
  config.json      settings
  proxies.txt      your proxies
  usernames.txt    your custom list
  blacklist.txt    names already checked
  valids.txt       every available name found
  stats.json       all-time totals
results/
  available-<time>.txt    names found in that session
logs/
  session-<time>.log      full log of that session
```

## Disclaimer

This project is for educational use and is not affiliated with Discord.
Sending automated requests in bulk may violate Discord's Terms of Service and can get your IP address rate-limited or blocked.
You are responsible for how you use it. Keep volumes low.

## License

Released under the [GPL-3.0](LICENSE) license.
