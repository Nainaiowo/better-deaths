# Better Deaths

Better Deaths is a Dalamud plugin for reviewing party deaths with pull-level context.

It records:

- party-wide death order
- likely death cause
- active statuses at death
- recent action timeline before each death
- recorded pull groups after wipes, recommences, and territory changes

## What It Does

Better Deaths is built around raid review after a pull ends. It keeps deaths grouped by pull, shows the timeline in death order, and expands each player into the details that matter for figuring out what happened.

It also includes:

- HP plus shields before the likely hit
- a compact HP bar with shields shown separately
- active mitigation, shields, protections, and boss damage-down debuffs
- recent action history before each death
- grouped pulls that remain available after wipes
- chat posts for a selected channel
- clickable recap links created from Better Deaths chat posts

## Commands

```text
/betterdeaths
/bd
```

## Dalamud Repository

Add this custom plugin repository URL in Dalamud:

```text
https://raw.githubusercontent.com/Nainaiowo/IMakeSillyThings/refs/heads/main/repo.json
```

Then install `Better Deaths` from Dalamud's plugin installer.
