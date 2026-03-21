# CharacterMover2D Notes

Slope handling — ground check via a horizontal OverlapBox does not reliably detect angled surfaces. Running downhill produces a "staircase" effect because grounded sub-states only output horizontal velocity, causing the character to repeatedly walk off the slope, fall, land, and repeat.