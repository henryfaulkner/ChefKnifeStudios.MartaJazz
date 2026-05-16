let mapPinImagesBaseUrl = "images/map-pins";

window.ChefMap = {
    popups: [],
    vehiclePopup: null,
    maps: {},

    createMap: async function (containerDivId, mapComponent) {
        let mapSettings = await mapComponent.invokeMethodAsync("getMapSettings");

        let map = new atlas.Map(containerDivId, {
            center: mapSettings.center,
            zoom: mapSettings.zoom,
            language: mapSettings.language,
            style: mapSettings.style,
            authOptions: {
                authType: "anonymous",
                clientId: mapSettings.mapAccClientId,
                getToken: function (resolve, reject, m) {
                    let xhttp = new XMLHttpRequest();
                    xhttp.open("GET", mapSettings.tokenApiUrl, true);
                    if (typeof (mapSettings.apiToken) === "string") {
                        xhttp.setRequestHeader("Authorization", `Bearer ${mapSettings.apiToken}`);
                    }
                    xhttp.onreadystatechange = function () {
                        if (this.readyState === 4 && this.status === 200) {
                            resolve(this.responseText);
                        } else if (this.status !== 200) {
                            reject(this.responseText);
                        }
                    };
                    xhttp.send();
                }
            }
        });

        this.maps[containerDivId] = map;

        // wait until the map resources are ready
        map.events.add('ready', async function () {
            await map.initDataSourceForVehiclePositions(containerDivId, mapComponent);

            let containerDiv = document.getElementById(containerDivId);

            if (containerDiv) {
                // notify map ready status
                // ensure that map ready status is triggered for correct map instance (different for each page)
                // prevent trigger if user moves to a different page, while map was still loading on the last page
                mapComponent.invokeMethodAsync("notifyMapReadyAsync");
            }
        });

        // close all popups when clicked on the map
        map.events.add('click', (e) => {
            let isPin = e.shapes && e.shapes.length > 0 && (typeof e.shapes[0].getType == 'function' && e.shapes[0].getType() === 'Point');

            if (!isPin && e.originalEvent) {
                try { isPin = e.originalEvent.target.tagName !== 'CANVAS'; }
                catch (e) { }
            }

            if (!isPin) {
                ChefMap.popups.forEach(p => p.close());
                mapComponent.invokeMethodAsync("mapBodyClickedAsync");
            }
        });
    },

    setMapZoom: function (containerDivId, zoom) {
        let map = ChefMap.maps[containerDivId];
        if (map == null) return;

        if (zoom >= 1 && zoom <= 24) {
            map.setCamera({ zoom: zoom });
        }
    },

    setMapStyle: function (containerDivId, mapStyle) {
        try {
            let map = ChefMap.maps[containerDivId];
            if (map == null) return;

            map.setStyle({ style: mapStyle });
        } catch { }
    },

    toggleTraffic: function (containerDivId, showTraffic) {
        let map = ChefMap.maps[containerDivId];
        if (map == null) return;

        map.setTraffic({
            incidents: showTraffic,
            flow: showTraffic ? 'relative' : 'none' // relative, relative-delay, absolute
        });
    },

    centerVehiclePin: function (containerDivId, vehicleId) {
        let map = ChefMap.maps[containerDivId];
        if (map == null) return;

        let ds = map.sources.getById('vehicles');
        if (ds == null) return;

        let shape = ds.getShapes().find(s =>
            s.getType() === 'Point' && s.getProperties().vehicleId === vehicleId
        );
        if (shape) map.setCamera({ center: shape.getCoordinates() });
    },

    plotFeatures: function (containerDivId, featureType, featureCollection, centerMap) {
        let sourceId = ((r) => ({
            "vehicles": "vehicles"
        })[r])(featureType);

        let map = ChefMap.maps[containerDivId];
        if (map == null) return;

        let datasource = map.sources.getById(sourceId);

        if (datasource != null) {
            // remove existing feature, if any
            datasource.clear();

            let features = featureCollection.features;

            if (features.length > 0) {
                datasource.add(featureCollection);

                if (centerMap === true) {
                    // center the map based on all features plotted
                    try {
                        if (featureType !== "vehicles") {
                            features = features.filter(f => f.geometry.type === "Point");
                        } else {
                            // use first few - map hangs when there are too many features
                            features = features.slice(0, 20);
                        }

                        let allCoordinates = features.map(f => [f.geometry.coordinates[0], f.geometry.coordinates[1]]);
                        let box = atlas.data.BoundingBox.fromPositions(allCoordinates);

                        map.setCamera({ center: atlas.data.BoundingBox.getCenter(box) });
                    } catch (e) { }
                }
            }
        }
    },

    addRouteShapeFeature: function (containerDivId, routeId, coordinates, color) {
        let map = ChefMap.maps[containerDivId];
        if (map == null) return;

        let ds = map.sources.getById('route-shapes');
        if (ds == null) return;

        let feature = new atlas.data.Feature(
            new atlas.data.LineString(coordinates.map(c => [c[0], c[1]])),
            { routeId: routeId, color: color || '#0078D4' },
            'route-' + routeId
        );
        ds.add(feature);
    },

    showRouteShape: function (containerDivId, geoJsonString) {
        let map = ChefMap.maps[containerDivId];
        if (map == null) return;

        let ds = map.sources.getById('route-shapes');
        if (ds == null) return;

        ds.clear();

        try {
            let feature = JSON.parse(geoJsonString);
            ds.add(feature);
        } catch (err) {
            console.warn('[ChefMap] showRouteShape: failed to parse GeoJSON', err);
        }
    },

    clearRouteShape: function (containerDivId) {
        let map = ChefMap.maps[containerDivId];
        if (map == null) return;

        let ds = map.sources.getById('route-shapes');
        if (ds == null) return;

        ds.clear();
    },

    _showBusTooltip: function (map, props, position) {
        if (!ChefMap._vehiclePopup) {
            ChefMap._vehiclePopup = new atlas.Popup({ closeButton: false });
        }
    }
};

atlas.Map.prototype.initDataSourceForVehiclePositions = async function (containerDivId, mapComponent) {
    let map = ChefMap.maps[containerDivId];
    if (map == null) return;

    let sourceId = 'vehicles';
    let dsVehicles = map.sources.getById(sourceId);

    if (dsVehicles != null) return;

    const pinImages = ['stop-pin-red', 'stop-pin-blue', 'stop-pin-green', 'stop-pin-orange'];
    await Promise.all(pinImages.map(id =>
        map.imageSprite.add(id, `${mapPinImagesBaseUrl}/${id}.png`)
    ));

    // Route shapes layer — added first so it renders below vehicle markers
    let routeDs = new atlas.source.DataSource('route-shapes');
    map.sources.add(routeDs);

    let routeLayer = new atlas.layer.LineLayer(routeDs, 'route-shapes-layer', {
        strokeColor: ['coalesce', ['get', 'color'], '#0078D4'],
        strokeWidth: 4,
        strokeOpacity: 0.85,
        lineJoin: 'round',
        lineCap: 'round'
    });

    map.layers.add(routeLayer);

    dsVehicles = new atlas.source.DataSource(sourceId);
    map.sources.add(dsVehicles);

    ChefMapAnimator._datasource = dsVehicles;

    let vehicleSymbolLayer = new atlas.layer.SymbolLayer(dsVehicles, 'vehicle-positions-layer', {
        iconOptions: {
            image: ['coalesce', ['get', 'pinIcon'], 'stop-pin-red'],
            size: 0.8,
            anchor: 'center',
            allowOverlap: true,
            ignorePlacement: true
        },
        filter: ['==', ['geometry-type'], 'Point']
    });

    map.layers.add(vehicleSymbolLayer);

    // change the hover cursor for pins to pointer
    map.events.add('mouseover', [vehicleSymbolLayer], () => {
        map.getCanvasContainer().style.cursor = 'pointer';
        document.querySelector('.atlas-map-canvas').style.cursor = 'pointer'
    });

    // change the cursor back to the default (grab)
    map.events.add('mouseout', [vehicleSymbolLayer], () => {
        map.getCanvasContainer().style.cursor = 'grab';
        document.querySelector('.atlas-map-canvas').style.cursor = 'grab'
    });
};
