window.OvercastMap = {
    // shapes: { [vehicleId]: atlas.Shape } — shapes live in the DataSource; store refs here for fast lookup.
    shapes: {},
    popups: [],
    maps: {},
    _busPopup: null,

    createMap: async function (containerDivId, mapComponent) {
        let mapSettings = await mapComponent.invokeMethodAsync("getMapSettings");

        let map = new atlas.Map(containerDivId, {
            center: mapSettings.center,
            zoom: mapSettings.zoom,
            language: mapSettings.language,
            style: mapSettings.style,
            renderingOptions: {
                renderAntialias: true
            },
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

        map.events.add('ready', async function () {
            await map.initDataSourceForBusPositions(containerDivId);

            let containerDiv = document.getElementById(containerDivId);
            if (containerDiv) {
                mapComponent.invokeMethodAsync("notifyMapReadyAsync");
            }
        });

        map.events.add('click', (e) => {
            let isPin = e.shapes && e.shapes.length > 0 && (typeof e.shapes[0].getType == 'function' && e.shapes[0].getType() === 'Point');

            if (!isPin && e.originalEvent) {
                try { isPin = e.originalEvent.target.tagName !== 'CANVAS'; }
                catch (e) { }
            }

            if (!isPin) {
                OvercastMap.popups.forEach(p => p.close());
                mapComponent.invokeMethodAsync("mapBodyClickedAsync");
            }
        });
    },

    setMapZoom: function (containerDivId, zoom) {
        let map = OvercastMap.maps[containerDivId];
        if (map == null) return;

        if (zoom >= 1 && zoom <= 24) {
            map.setCamera({ zoom: zoom });
        }
    },

    setMapStyle: function (containerDivId, mapStyle) {
        try {
            let map = OvercastMap.maps[containerDivId];
            if (map == null) return;

            map.setStyle({ style: mapStyle });
        } catch { }
    },

    toggleTraffic: function (containerDivId, showTraffic) {
        let map = OvercastMap.maps[containerDivId];
        if (map == null) return;

        map.setTraffic({
            incidents: showTraffic,
            flow: showTraffic ? 'relative' : 'none'
        });
    },

    upsertBusMarker: function (containerDivId, vehicleId, latitude, longitude) {
        if (latitude == null || longitude == null || isNaN(latitude) || isNaN(longitude)) {
            console.warn('[OvercastMap] upsertBusMarker: invalid coordinates for vehicle', vehicleId);
            return;
        }

        let map = OvercastMap.maps[containerDivId];
        if (map == null) return;

        let ds = map.sources.getById('bus-positions');
        if (ds == null) return;

        let existing = OvercastMap.shapes[vehicleId];
        if (existing) {
            existing.setCoordinates([longitude, latitude]);
        } else {
            let feature = new atlas.data.Feature(
                new atlas.data.Point([longitude, latitude]),
                { vehicleId: vehicleId },
                vehicleId
            );
            let shape = new atlas.Shape(feature);
            ds.add(shape);
            OvercastMap.shapes[vehicleId] = shape;
        }
    },

    _showBusTooltip: function (map, props, position) {
        if (!OvercastMap._busPopup) {
            OvercastMap._busPopup = new atlas.Popup({ closeButton: false });
        }
        let routeText = props.routeId ? `<br/>Route: ${props.routeId}` : '';
        let tsText = props.timestamp
            ? `<br/>${new Date(props.timestamp * 1000).toLocaleTimeString()}`
            : '';
        OvercastMap._busPopup.setOptions({
            content: `<div style="padding:4px 8px;font-size:12px">
                        <b>Vehicle: ${props.vehicleId}</b>${routeText}${tsText}
                      </div>`,
            position: position,
            pixelOffset: [0, -10]
        });
        OvercastMap._busPopup.open(map);
    },

    _hideBusTooltip: function () {
        if (OvercastMap._busPopup) OvercastMap._busPopup.close();
    },
};

atlas.Map.prototype.initDataSourceForBusPositions = async function (containerDivId) {
    let map = OvercastMap.maps[containerDivId];
    if (map == null) return;

    let sourceId = 'bus-positions';
    if (map.sources.getById(sourceId) != null) return;

    let ds = new atlas.source.DataSource(sourceId);
    map.sources.add(ds);

    try {
        await map.imageSprite.add('bus-pin', '/images/map-pins/stop-pin-green.png');
    } catch (err) {
        console.warn('[OvercastMap] Could not load bus-pin sprite:', err);
    }

    let busLayer = new atlas.layer.SymbolLayer(ds, 'bus-positions-layer', {
        iconOptions: {
            image: 'bus-pin',
            size: 0.8,
            anchor: 'center',
            allowOverlap: true,
            ignorePlacement: true
        },
        textOptions: {
            textField: ['get', 'vehicleId'],
            offset: [0, 1.2],
            color: 'white',
            size: 11,
            haloColor: '#1a1a2e',
            haloWidth: 2
        },
        filter: ['==', ['geometry-type'], 'Point']
    });

    map.layers.add(busLayer);

    map.events.add('mouseover', busLayer, (e) => {
        map.getCanvasContainer().style.cursor = 'pointer';
        if (!e.shapes || e.shapes.length === 0) return;
        let p = e.shapes[0].getProperties();
        OvercastMap._showBusTooltip(map, p, e.position);
    });

    map.events.add('mouseout', busLayer, () => {
        map.getCanvasContainer().style.cursor = 'grab';
        OvercastMap._hideBusTooltip();
    });

    map.events.add('dataremoved', ds, () => {
        OvercastMap.shapes = {};
    });
};
