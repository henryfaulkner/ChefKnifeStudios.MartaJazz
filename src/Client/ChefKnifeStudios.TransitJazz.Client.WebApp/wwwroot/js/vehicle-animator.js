window.ChefMapAnimator = {
    vehicles: {},
    routeGeometry: {},
    _datasource: null,
    _animFrameId: null,
    _running: false,

    // Throttled frame logging: log once per second
    _lastFrameLogTime: 0,

    _log: function (level, msg, data) {
        if (data !== undefined) {
            console[level]('[ChefMapAnimator] ' + msg, data);
        } else {
            console[level]('[ChefMapAnimator] ' + msg);
        }
    },

    haversineMeters: function (p1, p2) {
        var R = 6371000;
        var toRad = Math.PI / 180;
        var dLat = (p2[1] - p1[1]) * toRad;
        var dLon = (p2[0] - p1[0]) * toRad;
        var lat1 = p1[1] * toRad;
        var lat2 = p2[1] * toRad;
        var a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
        return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    },

    buildCumulativeDistances: function (coords) {
        var cumDist = [0];
        for (var i = 1; i < coords.length; i++) {
            cumDist.push(cumDist[i - 1] + this.haversineMeters(coords[i - 1], coords[i]));
        }
        return cumDist;
    },

    findNearestIndex: function (coords, point) {
        var minDist = Infinity;
        var minIdx = 0;
        for (var i = 0; i < coords.length; i++) {
            var d = this.haversineMeters(coords[i], point);
            if (d < minDist) {
                minDist = d;
                minIdx = i;
            }
        }
        return minIdx;
    },

    extractSubPath: function (routeCoords, startIdx, endIdx) {
        if (startIdx <= endIdx) {
            return routeCoords.slice(startIdx, endIdx + 1);
        }
        // Looping route: wrap around
        this._log('debug', 'extractSubPath: wrap-around detected', { startIdx: startIdx, endIdx: endIdx, routeLength: routeCoords.length });
        return routeCoords.slice(startIdx).concat(routeCoords.slice(0, endIdx + 1));
    },

    interpolateAlongPath: function (subPath, cumDist, t) {
        if (subPath.length === 0) return null;
        if (subPath.length === 1 || t <= 0) return subPath[0];
        if (t >= 1) return subPath[subPath.length - 1];

        var totalDist = cumDist[cumDist.length - 1];
        var targetDist = t * totalDist;

        var lo = 0;
        var hi = cumDist.length - 1;
        while (lo < hi - 1) {
            var mid = (lo + hi) >> 1;
            if (cumDist[mid] <= targetDist) lo = mid;
            else hi = mid;
        }

        var segStart = cumDist[lo];
        var segEnd = cumDist[hi];
        var segLen = segEnd - segStart;
        var segFrac = segLen > 0 ? (targetDist - segStart) / segLen : 0;

        var lon = subPath[lo][0] + (subPath[hi][0] - subPath[lo][0]) * segFrac;
        var lat = subPath[lo][1] + (subPath[hi][1] - subPath[lo][1]) * segFrac;
        return [lon, lat];
    },

    extrapolateAlongRoute: function (state, elapsedMs) {
        if (!state.speed || state.speed <= 0) return state.endPos;

        var routeData = this.routeGeometry[state.routeId];
        if (!routeData) return state.endPos;

        var extraDist = state.speed * (elapsedMs / 1000);
        var maxExtraDist = state.speed * 5;
        var clampedDist = Math.min(extraDist, maxExtraDist);

        var endIdx = this.findNearestIndex(routeData.coords, state.endPos);

        var remaining = clampedDist;
        for (var i = endIdx; i < routeData.coords.length - 1; i++) {
            var segDist = routeData.cumDist[i + 1] - routeData.cumDist[i];
            if (remaining <= segDist) {
                var frac = remaining / segDist;
                var lon = routeData.coords[i][0] + (routeData.coords[i + 1][0] - routeData.coords[i][0]) * frac;
                var lat = routeData.coords[i][1] + (routeData.coords[i + 1][1] - routeData.coords[i][1]) * frac;
                return [lon, lat];
            }
            remaining -= segDist;
        }

        this._log('debug', 'extrapolateAlongRoute: reached end of route for vehicle ' + state.vehicleId);
        return routeData.coords[routeData.coords.length - 1];
    },

    tick: function (now) {
        if (!this._running) return;

        var ds = this._datasource;
        if (!ds) {
            this._log('warn', 'tick: datasource not set, skipping frame');
            this._animFrameId = requestAnimationFrame(this.tick.bind(this));
            return;
        }

        var vehicleIds = Object.keys(this.vehicles);
        var activeCount = 0;
        var extrapolatingCount = 0;
        var idleCount = 0;
        var missingShapeCount = 0;

        for (var i = 0; i < vehicleIds.length; i++) {
            var state = this.vehicles[vehicleIds[i]];
            if (!state || state.phase === 'idle') {
                idleCount++;
                continue;
            }

            activeCount++;
            var elapsed = now - state.startTime;
            var newPos = null;

            if (state.phase === 'interpolating') {
                var t = Math.min(elapsed / state.duration, 1.0);
                newPos = this.interpolateAlongPath(state.subPath, state.subPathCumDist, t);

                if (t >= 1.0) {
                    state.endPos = newPos;
                    var nextPhase = state.speed ? 'extrapolating' : 'idle';
                    this._log('debug', 'vehicle ' + state.vehicleId + ': interpolation complete → ' + nextPhase);
                    state.phase = nextPhase;
                    state.startTime = now;
                }
            } else if (state.phase === 'extrapolating') {
                extrapolatingCount++;
                newPos = this.extrapolateAlongRoute(state, elapsed);
                if (elapsed > 30000) {
                    this._log('debug', 'vehicle ' + state.vehicleId + ': extrapolation timeout → idle');
                    state.phase = 'idle';
                }
            }

            if (newPos && (newPos[0] !== state.currentPos[0] || newPos[1] !== state.currentPos[1])) {
                state.currentPos = newPos;
                var shape = ds.getShapeById('vehicle-' + state.vehicleId);
                if (shape) {
                    shape.setCoordinates(newPos);
                } else {
                    missingShapeCount++;
                }
            }
        }

        // Log a summary once per second to avoid flooding the console
        if (now - this._lastFrameLogTime >= 1000) {
            this._lastFrameLogTime = now;
            this._log('debug', 'tick summary', {
                total: vehicleIds.length,
                active: activeCount,
                extrapolating: extrapolatingCount,
                idle: idleCount,
                missingShapes: missingShapeCount
            });
        }

        this._animFrameId = requestAnimationFrame(this.tick.bind(this));
    },

    start: function () {
        if (this._running) return;
        this._log('info', 'animation loop starting');
        this._running = true;
        this._animFrameId = requestAnimationFrame(this.tick.bind(this));
    },

    stop: function () {
        this._log('info', 'animation loop stopping');
        this._running = false;
        if (this._animFrameId) {
            cancelAnimationFrame(this._animFrameId);
            this._animFrameId = null;
        }
    },

    loadRouteGeometry: function (routeId, coordinates) {
        var cumDist = this.buildCumulativeDistances(coordinates);
        this.routeGeometry[routeId] = { coords: coordinates, cumDist: cumDist };
        this._log('debug', 'loadRouteGeometry: ' + routeId + ' (' + coordinates.length + ' coords, ' + Math.round(cumDist[cumDist.length - 1]) + 'm total)');
    },

    processNearestPointBatch: function (containerDivId, records) {
        this._log('debug', 'processNearestPointBatch: received ' + records.length + ' records for map ' + containerDivId);

        var map = ChefMap.maps[containerDivId];
        if (!map) {
            this._log('warn', 'processNearestPointBatch: map not found for containerDivId=' + containerDivId);
            return;
        }

        var ds = map.sources.getById('vehicles');
        if (!ds) {
            this._log('warn', 'processNearestPointBatch: vehicles datasource not found');
            return;
        }

        this._datasource = ds;
        if (!this._running) this.start();

        var now = performance.now();
        var newVehicles = 0;
        var updatedVehicles = 0;
        var teleportedVehicles = 0;
        var fallbackLerpVehicles = 0;

        for (var i = 0; i < records.length; i++) {
            var rec = records[i];
            var existingState = this.vehicles[rec.vehicleId];

            // Route transfer — teleport, don't animate
            if (existingState && existingState.routeId !== rec.routeId) {
                this._log('debug', 'vehicle ' + rec.vehicleId + ': route transfer ' + existingState.routeId + ' → ' + rec.routeId + ', teleporting');
                teleportedVehicles++;
                existingState = null;
            }

            var routeData = this.routeGeometry[rec.routeId];
            var subPath, subPathCumDist, totalDistance;

            if (routeData) {
                var startIdx = this.findNearestIndex(routeData.coords, [rec.priorLon, rec.priorLat]);
                var endIdx = this.findNearestIndex(routeData.coords, [rec.currentLon, rec.currentLat]);
                this._log('debug', 'vehicle ' + rec.vehicleId + ': route=' + rec.routeId + ' startIdx=' + startIdx + ' endIdx=' + endIdx);
                subPath = this.extractSubPath(routeData.coords, startIdx, endIdx);
                subPathCumDist = this.buildCumulativeDistances(subPath);
                totalDistance = subPathCumDist[subPathCumDist.length - 1];
            } else {
                this._log('warn', 'vehicle ' + rec.vehicleId + ': no geometry for route ' + rec.routeId + ', falling back to straight-line lerp');
                fallbackLerpVehicles++;
                subPath = [[rec.priorLon, rec.priorLat], [rec.currentLon, rec.currentLat]];
                subPathCumDist = this.buildCumulativeDistances(subPath);
                totalDistance = subPathCumDist[1] || 0;
            }

            var startPos = existingState ? existingState.currentPos : [rec.priorLon, rec.priorLat];

            // Smooth handoff: if mid-animation, start from current rendered position
            if (existingState && existingState.phase !== 'idle' && subPath.length > 1) {
                this._log('debug', 'vehicle ' + rec.vehicleId + ': mid-animation handoff from ' + JSON.stringify(startPos));
                subPath[0] = startPos;
                subPathCumDist = this.buildCumulativeDistances(subPath);
                totalDistance = subPathCumDist[subPathCumDist.length - 1];
            }

            var phase = totalDistance > 0 ? 'interpolating' : 'idle';
            this._log('debug', 'vehicle ' + rec.vehicleId + ': ' + phase + ', dist=' + Math.round(totalDistance) + 'm, duration=' + Math.round(rec.durationMs || 10000) + 'ms, waypoints=' + subPath.length);

            this.vehicles[rec.vehicleId] = {
                vehicleId: rec.vehicleId,
                routeId: rec.routeId,
                subPath: subPath,
                subPathCumDist: subPathCumDist,
                totalDistance: totalDistance,
                startTime: now,
                duration: rec.durationMs || 10000,
                speed: rec.speed || null,
                bearing: rec.bearing || null,
                currentPos: startPos,
                endPos: subPath[subPath.length - 1],
                phase: phase
            };

            // Ensure feature exists in datasource
            var shape = ds.getShapeById('vehicle-' + rec.vehicleId);
            if (!shape) {
                this._log('debug', 'vehicle ' + rec.vehicleId + ': creating new map feature at ' + JSON.stringify(startPos));
                newVehicles++;
                ds.add(new atlas.data.Feature(
                    new atlas.data.Point(startPos),
                    { vehicleId: rec.vehicleId, pinIcon: 'stop-pin-green' },
                    'vehicle-' + rec.vehicleId
                ));
            } else {
                updatedVehicles++;
            }
        }

        this._log('info', 'processNearestPointBatch complete', {
            records: records.length,
            newFeatures: newVehicles,
            updated: updatedVehicles,
            teleported: teleportedVehicles,
            fallbackLerp: fallbackLerpVehicles
        });
    }
};
