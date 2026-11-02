# Fusing originals while preserving their identity

By default a fusion keeps one game as the base and brings another's behaviors into its world.
Direction matters: reversing the base creates a different fusion. The player should recognize
the base's maps, graphics, rules, interface and rhythm. Creation with originals is a separate
mode opened from Create: it designs a new scene from those resources.

## 1. Understand each original

Identify its main action, objective, risk, resource and recognizable rule. For the base,
also identify invariant maps, layout, scale, palette, animation, typography, scores, menus
and sounds. Separate these from contributions. Do not change every system to justify an idea.
Look for shared consequences between rules, not visual resemblance. Apply the common design
contract included in the request.

### Data preparation

If the central interaction lacks evidence, list the concrete fact and pending experiment
in missingData: a normal action that exposes it, a comparison that tests it and what will
remain unproven. The studio can boot originals and perform bounded input experiments;
do not require manual play as the starting condition.

Prepare and create this fusion preserves originals, request, base and chosen mechanic.
Creation calls it Prepare and create this game. An initial estimate is approximate.
With connected AI, multiple research rounds may use up to an hour for the whole job.
The robot prepares that edition's data; AI reevaluates the same idea using current profiles.
When evidence is missing, test history and the reached game state guide further experiments.
A new writing requirement returns to research without changing the idea. Continue only
with support. A time limit or lack of new evidence preserves results and explains missing
facts. Existing profiles or successful boot do not prove everything solved.

Cite actual variables, controls, resources and helpers for each requirement. Screen position
does not prove world coordinates, camera, solidity, collision boxes, damage or progress.
An effective write does not prove its meaning. Do not clear missingData merely to enable
creation, lower requirements or substitute an easier idea.

Experiments use permitted inputs, not host code or guessed addresses. Unverified maps and
rules remain missing. This does not authorize pasted screens or a generic replacement base.

## 2. Find the mechanical connection

Describe an inseparable rule: how an action from one game changes decisions in the other.
Give ten concrete seconds of play.

- Removing either original must change the main loop. Graphics or sound alone are insufficient.
- Respect the declared base. Bring movements, enemies, actions, resources or rules into its
  world. Keep or change protagonists as the design requires, but they must affect real base
  situations through the other's behavior.
- Only explicit free reinterpretation may lack a dominant original or inherited genre.
- Choose a protagonist, group, board or system according to the design. Not every game needs
  a hero, lives, shooting, platforms or enemies.
- Preserve recognizable traits, transform rules and explain omissions. Never invent addresses
  or profile capabilities.

## 3. Distinguish ideas and existing versions

Compare the supplied previous fusions. New names, colors, banners or HUD positions are not
new ideas. Change the added decision loop or relationship. Keeping the selected base's map,
palette and HUD is correct, not template repetition. Preserve the game for a small repair;
explain the actual difference when asked for a new fusion.

For each proposal state its mechanical union, unique rule, visual/play identity and
difference from other ideas and earlier fusions.

## 4. Design play and presentation together

Define objective, controls and ending appropriate to the genre. Progression may use an
evolving room, rounds, a continuous route, map, campaign or short session. No level count
or menu template is mandatory.

In base mode inherit its flow, goals, controls and progression first. Additional maps require
verified structures and tiles at the same scale and visual language. Missing map formats
are pending data, not permission to substitute generic rectangle levels.

Titles and transitions may be integrated into the world. Teach through animation, sound,
environmental changes or contextual instructions instead of stopping for every banner.
The HUD may be lateral, minimal, in-world or unnecessary. Show information where and when
it helps the player.

Emulator messages and automatic-test states do not mandate game menus. Preserve the base's
visual language, without universal title screens, cards or HUDs covering its own.

## 5. Recognizable space and clear controls

- The engine keeps the base image before draw. screen.base(games.<base>) restores it.
  Map expansion needs verified format and memory changes while preserving its engine.
  Contributions share its coordinates, scale, boundaries and collisions rather than another
  playable window.
- Free reinterpretation draws its chosen scene using original sprites, tiles and data.
- g.capture preserves a small verified multicolor image resource. Cache and reuse it.
  The limits prohibit capturing a full screen. Capture does not establish motion or collision.
- Do not clear or resize the base. Missing character or map data does not authorize an easier
  generic replacement.
- screen.game remains retired. Do not paste contributors' whole screens or thumbnails.
  g.pixel observes state; it is not a screen-copying tool.
- Keep space, threats and interactive objects readable. There is no mandatory enemies-above,
  platforms-below layout. Do not hide players or hazards behind terrain.
- Use few explained controls. Distinguish jump and fire when both exist.
- Do not copy an earlier fusion's complete presentation. Inheriting the base is intended;
  new contributions must fit it.

Before coding, describe how a contribution changes the best action in the base and how
the base constrains that action. If removing one original removes only decoration or a
separate minigame, redesign the connection. During revisions preserve recognizable behavior;
tune response, collision, rhythm and communication before adding systems.

## 6. Instrumentation

Report game.state('title'), game.state('playing'), game.state('gameover'), game.score,
game.death and game.level as appropriate. This contract does not demand three screens,
three levels or a conventional scoreboard.

Base mode may begin in playing, preserve native entry or introduce within the world.
Report real objectives rather than inventing points. In free mode, entry communicates
objective and controls and allows start, Space or fire. Endings allow start to reset
variables and original machines. Level may represent a phase or round without cards.

## 7. Use original machines deliberately

Reuse behaviors that support the design. A machine may remain paused when its resources
or data implement its rule elsewhere, but every original needs an identifiable mechanic.
Control hidden effects with pause, restart, speed, volume and documented profile helpers.
Use a single coordinate conversion for drawing and collision. State missing facts.

A ROM's availability does not establish knowledge of its routines. Use supplied variables,
controls, sprites, helpers and fingerprints. Captures and filenames do not justify invented
addresses. An unsupported central interaction must be prepared before implementation.

g.set is a one-time write the original may overwrite next frame. g.freeze maintains a value
during CPU writes; g.release removes that constraint. Freeze only when required, avoiding
counters needed for transitions. Respect ranges, masks and read-only fields. Test game state,
graphics buffer and collision separately: moving a sprite does not prove its entity moved.
Also test release and restart. These functions apply to fusions and creation with originals.

## 8. Fairness, learning and tests

Begin with an understandable, avoidable risk. Teach one new rule at a time in a form suitable
for this game. Avoid invisible traps, lethal respawns and decision-free victories.

Idle, walking and shooting robots detect some failures; they do not judge creativity or all
genres. A puzzle need not shoot and a race need not have deaths. Explain coverage limits and
test difficult decisions directly. Missing points remain an observation, not permission to
fake them. Reading a contribution's resource does not certify mechanical integration:
compare an observable consequence with a control lacking that rule.

Inspect captures and test controls, progress, ending and restart. Clarify a confusing rule
before adding content. Investigate pending facts with bounded automatic experiments;
manual player completion is not a prerequisite for autonomous work.

## Directed playtests

playtests repeat inputs and measure memory from the host. Every route resets machines and
script. They accept no code, addresses, writes or prepared states. Slots and variable names
must exist in that edition's profiles.

~~~json
"playtests": [{
  "name": "One press retains the change while held",
  "steps": [{"ticks": 60, "inputs": ["fire"]}],
  "checks": [
    {"slot":"b","variable":"polarity","fromTick":0,"atTick":6,"min":1,"max":1,"measure":"delta"},
    {"slot":"b","variable":"polarity","fromTick":6,"atTick":60,"min":0,"max":0,"measure":"changes"}
  ]
}]
~~~

This example applies only if the profile really has polarity starting at zero with that
rule. The second check detects oscillation during the hold even if the final value returns.

| measure | Value compared with min/max |
|---|---|
| delta (legacy default) | Final minus initial value. |
| minDelta | Smallest change relative to the initial value, including initial zero. |
| maxDelta | Largest change relative to the initial value, including initial zero. |
| changes | Count of differences between consecutive ticks, from zero to interval duration. |

Extrema detect brief jumps or impacts that return to the initial value. Reports retain the
extreme and first observed tick. Sampling occurs once per 1/60-second tick; it does not count
every CPU write or see oscillations completed between samples. For wrapping fields, use a
verified composite variable or an interval that does not cross the wrap.

Tick zero is after init. inputs:[] releases everything. Steps last 1–600 ticks.
Each route permits 64 steps, 5400 ticks (90 seconds) and 1–12 checks, with up to three routes.

Choose discriminating cases: press/hold, correct/wrong effect, reward/cap, transition and
restart. Do not widen bounds to hide a mismatch. Reports preserve before/after and the profile's
observed, declared or hypothetical meaning. Memory reads prevent log or score from faking the
measurement; they do not prove an address's meaning or causality between originals.
Without observable data use [] and specify the next automatic experiment.

Mismatches return through ordinary correction and survive in recoverable drafts. Agent
folders import them from juego.json; saving it retests even if juego.js is unchanged.
Partially written JSON waits for completion instead of validating stale expectations.
Passing selected routes does not prove every level, ending, balance or fun.

## Checklist

- Removing either original changes a central decision.
- The unique rule and ten seconds of play can be described concretely.
- The added loop or relationship differs from previous ideas.
- Base maps, graphics, rules and interface remain recognizable.
- Menus, HUD and progression preserve the request without a universal template.
- Only the declared base supplies a full native image; others supply elements and behaviors.
- Controls, objective and ending are understandable and restart works.
- Instrumentation supports testing without dictating presentation.
- Captures and play were reviewed; technical validation does not prove creative quality.
- Directed routes distinguish real effects and state remaining rules or endings to test.

Avoid whole-game overlays, unrelated alternating games, cosmetic-only contributions,
universal three-level/HUD templates, erased base maps, renamed technical examples,
invented memory data, hidden hazards and quality claims based solely on robot approval.
