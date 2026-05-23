// checkpoint-tracker.js — crossing detection for 009-transit-soundscape
// Consumes per-tick position events from ChefMapAnimator; dispatches CrossingEvent batches to C#.

const _routeTriggerPoints = new Map();  // routeId → TriggerPoint[]
const _vehicleState = new Map();         // vehicleId → { routeId, lastTriggeredIndex, lastTriggerTimeMs }
let _dotNetRef = null;
let _tickHookInstalled = false;

const COOLDOWN_MS = 2000;
// Teleport threshold: > 2km along cumDist in one tick is treated as a GPS glitch
const TELEPORT_DIST_M = 2000;

function log(msg) {
    console.log('[CheckpointTracker] ' + msg);
}

export function configureRoute(routeId, triggerPoints, dotNetRef) {
    _routeTriggerPoints.set(routeId, triggerPoints);

    if (dotNetRef && !_dotNetRef) {
        _dotNetRef = dotNetRef;
    }

    if (!_tickHookInstalled) {
        _installTickHook();
        _tickHookInstalled = true;
        log('tick hook installed');
    }
}

export function clear() {
    _routeTriggerPoints.clear();
    _vehicleState.clear();
    _dotNetRef = null;
    _tickHookInstalled = false;
    _uninstallTickHook();
    log('cleared');
}

// Called at end of each ChefMapAnimator tick with vehicles that moved this frame.
// positionEvents: Array<{ vehicleId: string, routeId: string, currIndex: number }>
export function onTick(positionEvents) {
    if (!positionEvents || positionEvents.length === 0) return;
    if (!_dotNetRef) return;

    const batch = [];
    const now = performance.now();

    for (const ev of positionEvents) {
        const { vehicleId, routeId, currIndex } = ev;
        const triggers = _routeTriggerPoints.get(routeId);
        if (!triggers || triggers.length === 0) continue;  // FR-011: route geometry not yet loaded

        const state = _vehicleState.get(vehicleId);

        if (!state) {
            // FR-009: first observation — baseline at current index, fire nothing
            _vehicleState.set(vehicleId, {
                routeId,
                lastTriggeredIndex: currIndex,
                lastTriggerTimeMs: 0,
            });
            continue;
        }

        if (state.routeId !== routeId) {
            // Vehicle transferred routes — reset, fire nothing
            state.routeId = routeId;
            state.lastTriggeredIndex = currIndex;
            state.lastTriggerTimeMs = 0;
            continue;
        }

        const delta = currIndex - state.lastTriggeredIndex;

        if (delta <= 0) continue;  // no forward movement or direction reversal

        // FR-010: teleport check using the route's cumDist (if available via animator's routeGeometry)
        const routeGeom = window.ChefMapAnimator?.routeGeometry?.[routeId];
        if (routeGeom && routeGeom.cumDist) {
            const distAdvanced = routeGeom.cumDist[currIndex] - routeGeom.cumDist[state.lastTriggeredIndex];
            if (distAdvanced > TELEPORT_DIST_M) {
                state.lastTriggeredIndex = currIndex;
                state.lastTriggerTimeMs = 0;
                continue;
            }
        }

        // Find trigger points in (lastTriggeredIndex, currIndex]
        for (const tp of triggers) {
            if (tp.index > state.lastTriggeredIndex && tp.index <= currIndex) {
                // FR-007: cooldown suppression
                if ((now - state.lastTriggerTimeMs) >= COOLDOWN_MS) {
                    batch.push({ vehicleId, routeId, triggerIndex: tp.index });
                    state.lastTriggerTimeMs = now;
                }
            }
        }

        state.lastTriggeredIndex = currIndex;
    }

    if (batch.length > 0) {
        // Sort per contracts/interop-surface.md: (routeId, vehicleId, triggerIndex)
        batch.sort((a, b) =>
            a.routeId.localeCompare(b.routeId) ||
            a.vehicleId.localeCompare(b.vehicleId) ||
            a.triggerIndex - b.triggerIndex
        );
        _dotNetRef.invokeMethodAsync('OnCrossingsAsync', batch).catch(err => {
            console.warn('[CheckpointTracker] OnCrossingsAsync failed:', err);
        });
    }
}

let _originalTick = null;

function _installTickHook() {
    if (!window.ChefMapAnimator) return;
    _originalTick = window.ChefMapAnimator.tick.bind(window.ChefMapAnimator);

    window.ChefMapAnimator.tick = function (now) {
        _originalTick(now);

        // Build position events for vehicles that have a current position and moved
        const positionEvents = [];
        const vehicles = window.ChefMapAnimator.vehicles;
        for (const vehicleId of Object.keys(vehicles)) {
            const state = vehicles[vehicleId];
            if (!state || state.phase === 'idle') continue;
            if (!state.currentPos || !state.routeId) continue;

            const routeGeom = window.ChefMapAnimator.routeGeometry[state.routeId];
            if (!routeGeom) continue;

            const currIndex = window.ChefMapAnimator.findNearestIndex(routeGeom.coords, state.currentPos);
            positionEvents.push({ vehicleId: state.vehicleId, routeId: state.routeId, currIndex });
        }

        if (positionEvents.length > 0) {
            window.CheckpointTracker.onTick(positionEvents);
        }
    }.bind(window.ChefMapAnimator);
}

function _uninstallTickHook() {
    if (window.ChefMapAnimator && _originalTick) {
        window.ChefMapAnimator.tick = _originalTick;
        _originalTick = null;
    }
}

window.CheckpointTracker = { configureRoute, clear, onTick };
