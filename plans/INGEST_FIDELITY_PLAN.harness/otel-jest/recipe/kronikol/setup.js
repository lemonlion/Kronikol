'use strict'
// Jest setupFiles: the spans first, so their instrumentation is in place before anything loads the service.
require('./tracing')
require('./capture')
