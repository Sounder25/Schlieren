# Schlieren Brand Assets

Source brand board: `brand-board-source.png`  
(Original: Screenshots / Screenshot 2026-08-01 182453.png)

## Color palette

| Token | Hex | Role |
|-------|-----|------|
| Execution Indigo | `#4A00E0` | Primary actions, trust |
| Blob Aqua | `#19D7E5` | Flow, highlights, line numbers |
| Tracing White | `#F0F4F8` | Primary text on dark chrome |
| Warm Access Yellow | `#FFD700` | Positive / warm access |
| Cold Access Grey | `#A9A9A9` | Muted / inactive |
| Log / Revert Orange | `#FF4500` | Errors, reverts, alerts |

## Files (placement-specific)

| File | Placement | Density |
|------|-----------|---------|
| `schlieren-icon.png` | Window icon + header tile (32–48px) | **Simple** — thick strokes, few shapes |
| `schlieren-watermark.png` | Center panel background watermark (~300px) | **Full** — board mark at soft opacity |
| `schlieren-logo-full.png` | About / marketing large | **Full** |
| `schlieren-lockup.png` | Horizontal mark + wordmark | Medium |
| `brand-board-source.png` | Design reference only | Full board |

### Logo density rule

- **Small chrome** → simple icon only (avoids smear).
- **Large soft watermark** → full mark OK; keep opacity low so code stays readable.
- Watermark is `IsHitTestVisible=False` and fades further when files/trace are present.

## Typography (board)

- UI / wordmark: Montserrat (fallback: Inter, Segoe UI)
- Code: Source Code Pro (fallback: Consolas, Cascadia Mono)

## Values

Precise · Verifiable · Traceable · Conformant
