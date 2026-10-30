# AutoCommand III — v2026.10.30.11

Hotfix for v2026.10.30.10.

## Fixed

- **"A UI error occurred" when opening the Blocks tab** (and the Block
  bulk-preview window): the theme sweep in .30.10 replaced the local
  `BlocksMutedBrush`/`MutedBrush` XAML definitions with the theme-aware
  `MutedTextBrush`, but seven code-behind lookups fetched the old brushes
  by NAME STRING (`FindResource("BlocksMutedBrush")`) — invisible to both
  the text sweep and the compiler, so they threw at view load. All seven
  now point at `MutedTextBrush`, and an audit of every string-keyed
  `FindResource` in the code-behind confirms no other lookup references a
  missing key.

No other changes from v2026.10.30.10.
