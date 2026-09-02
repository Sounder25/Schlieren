# Video Edit Plan — Guard Demo

## Source Videos

| Video | Duration | Content |
|-------|----------|---------|
| Fresh Token pull 1132.mp4 | 18.7s | Quick token pull + Guard execution |
| 2 comp video.mp4 | 121.5s (2:01) | Full competitor comparison: Honeypot.is + GoPlus + Guard |

**Target: 3-minute demo video**

---

## Cut Plan

### Video 1: Fresh Token pull (18.7s → ~12s)
**Keep:**
- 0:00-0:03 — Token contract visible
- 0:04-0:08 — DexScreener pair info
- 0:09-0:14 — Guard execution result

**Cut:**
- Dead air, loading screens, cursor movement

### Video 2: 2 comp video (121.5s → ~90s)
**Keep:**
- 0:00-0:15 — Token introduction
- 0:15-0:35 — Honeypot.is showing "NOT A HONEYPOT, 0% tax, LOW RISK"
- 0:35-0:50 — GoPlus 404 NOT FOUND
- 0:50-1:20 — Guard execution with round-trip loss
- 1:20-1:30 — Result summary

**Cut:**
- Long pauses
- Repeated checks
- Cursor wandering
- Loading screens

---

## Final Structure (3:00)

| Time | Content | Source |
|------|---------|--------|
| 0:00-0:15 | Token intro + age | 2 comp video |
| 0:15-0:35 | Honeypot.is "safe" | 2 comp video |
| 0:35-0:50 | GoPlus 404 | 2 comp video |
| 0:50-1:20 | Guard execution | 2 comp video |
| 1:20-1:30 | Result overlay | Fresh token pull |
| 1:30-2:00 | Second token (if available) | TBD |
| 2:00-2:30 | Value prop narration | Voiceover |
| 2:30-3:00 | CTA | Text + logo |

---

## Edit Notes

**From frame analysis:**

**Fresh Token pull:**
- Frame 001-003: Token contract visible
- Frame 004-007: DexScreener
- Frame 008-012: Honeypot check
- Frame 013-019: Guard result

**2 comp video:**
- Frame 001-010: Token info
- Frame 011-025: Honeypot.is
- Frame 026-040: GoPlus 404
- Frame 041-061: Guard execution

---

## Tools Needed

- FFmpeg for precise cuts
- Voiceover recording (user)
- Text overlays for competitor comparisons

## Next Steps

1. Extract timestamps from frames where action happens
2. Create FFmpeg cut list
3. Apply cuts
4. Add voiceover
5. Export final demo
