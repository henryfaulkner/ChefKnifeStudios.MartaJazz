window.ChefPerfObserver = {
    _observer: null,
    _label: '',

    start: function (label) {
        if (this._observer) {
            this._observer.disconnect();
        }
        this._label = label;
        try {
            this._observer = new PerformanceObserver(function (list) {
                list.getEntries().forEach(function (entry) {
                    console.log('[perf:' + label + '] longtask ' + Math.round(entry.duration) + 'ms @ ' + Math.round(entry.startTime) + 'ms');
                });
            });
            this._observer.observe({ entryTypes: ['longtask'] });
        } catch (e) {
            console.warn('[ChefPerfObserver] PerformanceObserver for longtask not supported:', e);
        }
    },

    stop: function () {
        if (this._observer) {
            this._observer.disconnect();
            this._observer = null;
        }
    },

    mark: function (name) {
        performance.mark(name);
    },

    measure: function (name, startMark, endMark) {
        try {
            performance.measure(name, startMark, endMark);
            var entries = performance.getEntriesByName(name);
            return entries.length > 0 ? entries[entries.length - 1].duration : 0;
        } catch (e) {
            console.warn('[ChefPerfObserver] measure failed:', e);
            return 0;
        }
    }
};
