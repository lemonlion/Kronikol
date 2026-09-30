'use strict'
// V4's setupFilesAfterEnv entry: undo the process-wide subscription when the file ends (see v2).
afterAll(() => globalThis.__kronikolSpike.fastifyOtel.disable())
