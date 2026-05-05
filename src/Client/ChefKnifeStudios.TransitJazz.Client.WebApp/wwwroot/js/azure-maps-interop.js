let mapPinImagesBaseUrl = "/images/map-pins";

window.OvercastMap = {
    markers: {},
    popups: [],
    jobSitePopup: null,
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
            // configure data sources and layers
            await map.initDataSourceForJobsitePins(containerDivId);

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
            flow: showTraffic ? 'relative' : 'none' // relative, relative-delay, absolute
        });
    },

    centerJobsitePin: function (containerDivId, jobsiteId) {
        let map = OvercastMap.maps[containerDivId];
        if (map == null) return;

        let markerId = 'job_site_marker_' + jobsiteId;
        let obj = OvercastMap.markers[markerId];

        if (!obj || obj.marker == null || obj.shape == null) return;

        map.setCamera({ center: obj.shape.getCoordinates() });
    },

    plotFeatures: function (containerDivId, featureType, featureCollection, centerMap) {
        let sourceId = ((r) => ({
            "jobs": "job-sites"
        })[r])(featureType);

        let map = OvercastMap.maps[containerDivId];
        if (map == null) return;

        let datasource = map.sources.getById(sourceId);

        if (datasource != null) {
            // remove existing feature, if any
            datasource.clear();

            let features = featureCollection.features;

            //if (featureType == "jobs") console.log(`plotFeatures - ${featureType}: ${features.length}`);

            if (features.length > 0) {
                datasource.add(featureCollection);

                if (centerMap === true) {
                    // center the map based on all features plotted
                    try {
                        if (featureType !== "jobs") {
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

    toggleJobSiteMarkerActiveState: function (containerDivId, accountId, activate) {
        let markerId = 'job_site_marker_' + accountId;
        let obj = OvercastMap.markers[markerId];

        if (!obj || obj.marker == null || obj.shape == null) return;

        let marker = obj.marker;
        let shape = obj.shape;

        if (shape.getType() === 'Point') { // may not need this, as there is a filter on the related layer 
            let p = shape.getProperties();
            // let pinImage = activate ? p.pinIcon.replace('.png', '-active.png') : p.pinIcon;
            let pinImage = activate ? "stop-pin-orange.png" : p.pinIcon;
            let className = activate ? "active" : "";
            let tooltip = activate ? p.waJobsiteName : "";

            let attributes = [
                `id="${markerId}"`,
                `title="${tooltip}"`,
                `class="job-site-pin ${className}"`,
                `style="background-image: url('${mapPinImagesBaseUrl}/${pinImage}');"`
            ].join(' ');

            let markerContent = `<div ${attributes}><div>{text}<div></div>`;
            marker.setOptions({ htmlContent: markerContent });
        }
    },

    toggleJobsiteCardPopup: function (containerDivId, accountId, activate, centerMap) {
        let map = OvercastMap.maps[containerDivId];
        if (map == null) return;

        let markerId = 'job_site_marker_' + accountId;
        let obj = OvercastMap.markers[markerId];

        if (!obj || obj.marker == null) return;

        let marker = obj.marker;

        if (activate) {
            if (OvercastMap.jobSitePopup == null) {
                OvercastMap.jobSitePopup = new atlas.Popup({
                    pixelOffset: [110, -20], // to be set based on required position of popup relative to the marker
                    closeButton: false,
                    showPointer: false
                });

                OvercastMap.popups.push(OvercastMap.jobSitePopup);
            }

            if (OvercastMap.jobSitePopup.isOpen()) OvercastMap.jobSitePopup.close();

            let content = obj.infoContent;

            OvercastMap.jobSitePopup.setOptions({
                // update the content of the popup
                content: content,

                // update the popup's position with the symbol's coordinate
                position: marker.getOptions().position
            });

            // open the popup
            if (!OvercastMap.jobSitePopup.isOpen()) OvercastMap.jobSitePopup.open(map);

            if (centerMap === true) {
                map.setCamera({ center: marker.getOptions().position });
            }
        } else {
            if (OvercastMap.jobSitePopup.isOpen()) OvercastMap.jobSitePopup.close();
        }
    }
};

atlas.Map.prototype.initDataSourceForJobsitePins = async function (containerDivId) {
    let map = OvercastMap.maps[containerDivId];
    if (map == null) return;

    let sourceId = 'job-sites';
    let dsJobSites = map.sources.getById(sourceId);

    if (dsJobSites != null) return;

    dsJobSites = new atlas.source.DataSource(sourceId);
    map.sources.add(dsJobSites);

    // layer for rendering job sites
    let jobsiteSymbolLayer = new atlas.layer.SymbolLayer(dsJobSites, null, {
        iconOptions: { image: '' },
        filter: [  // only render Point in this layer
            'any',
            ['==', ['geometry-type'], 'Point']
        ]
    });

    map.layers.add(jobsiteSymbolLayer);

    // show job site pin symbol as HTML marker
    map.events.add('dataadded', dsJobSites, (e) => {
        let shapes = dsJobSites.getShapes();
        for (let i = 0; i < shapes.length; i++) {
            let s = shapes[i];
            if (s.getType() === 'Point') {
                let p = s.getProperties();
                let markerId = `job_site_marker_${p.waJobsiteId}`;

                // skip if marker already exists
                if (OvercastMap.markers[markerId]) continue;

                let pinImage = p.pinIcon ?? 'stop-pin-red.png';

                let attributes = [
                    `id="${markerId}"`,
                    `class="job-site-pin"`,
                    `style="background-image: url('${mapPinImagesBaseUrl}/${pinImage}');"`
                ].join(' ');

                let marker = new atlas.HtmlMarker({
                    htmlContent: `<div ${attributes}><div>{text}</div></div>`,
                    position: s.getCoordinates(),
                    anchor: 'center'
                });

                OvercastMap.markers[markerId] = {
                    marker: marker,
                    shape: s,
                    infoContent: `<jobsite-card-component id="${p.waJobsiteId}"></jobsite-card-component>`
                };

                map.events.add('click', marker, async () => {
                    OvercastMap.toggleJobsiteCardPopup(containerDivId, p.waJobsiteId, true);
                });

                map.events.add('mouseover', marker, async () => {
                    OvercastMap.toggleJobSiteMarkerActiveState(containerDivId, p.waJobsiteId, true);
                });

                map.events.add('mouseout', marker, async () => {
                    OvercastMap.toggleJobSiteMarkerActiveState(containerDivId, p.waJobsiteId, false);
                });

                map.markers.add(marker);
            }
        }
    });

    // remove HTML marker for job site when related feature is removed
    map.events.add('dataremoved', dsJobSites, (e) => {
        for (let markerId in OvercastMap.markers) {
            let obj = OvercastMap.markers[markerId];
            if (obj && obj.marker) {
                map.markers.remove(obj.marker);
            }
            OvercastMap.markers[markerId] = null;
        }
    });

    // change the hover cursor for pins to pointer
    map.events.add('mouseover', [jobsiteSymbolLayer], () => {
        map.getCanvasContainer().style.cursor = 'pointer';
        document.querySelector('.atlas-map-canvas').style.cursor = 'pointer'
    });

    // change the cursor back to the default (grab)
    map.events.add('mouseout', [jobsiteSymbolLayer], () => {
        map.getCanvasContainer().style.cursor = 'grab';
        document.querySelector('.atlas-map-canvas').style.cursor = 'grab'
    });
}