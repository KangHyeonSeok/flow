const express = require('express');
const path = require('path');
const { createSpecReader } = require('./lib/specReader');
const { createSpecWriter } = require('./lib/specWriter');
const { createActivityLogger } = require('./lib/activityLogger');
const { createRunner } = require('./lib/runner');

const app = express();
const PORT = process.env.PORT || 3000;
const SPECS_DIR = process.env.FLOW_SPECS_DIR || path.join(require('os').homedir(), '.flow', 'specs');

app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));

const reader = createSpecReader(SPECS_DIR);
const writer = createSpecWriter(SPECS_DIR);
const logger = createActivityLogger(SPECS_DIR);
const runner = createRunner(SPECS_DIR, reader, writer, logger);

// --- API Routes ---

// List all specs (card-level summary)
app.get('/api/specs', async (req, res) => {
  try {
    const specs = await reader.listSpecs();
    res.json(specs);
  } catch (err) {
    console.error('GET /api/specs error:', err);
    res.status(500).json({ error: err.message });
  }
});

// Get full spec detail
app.get('/api/specs/:specId', async (req, res) => {
  try {
    const spec = await reader.getSpec(req.params.specId);
    if (!spec) return res.status(404).json({ error: 'Spec not found' });
    res.json(spec);
  } catch (err) {
    console.error(`GET /api/specs/${req.params.specId} error:`, err);
    res.status(500).json({ error: err.message });
  }
});

// Get activity log
app.get('/api/specs/:specId/activity', async (req, res) => {
  try {
    const log = await reader.getActivity(req.params.specId);
    res.json(log);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// Serve evidence files
app.get('/api/specs/:specId/evidence/:fileName', async (req, res) => {
  try {
    const filePath = reader.getEvidencePath(req.params.specId, req.params.fileName);
    if (!filePath) return res.status(404).json({ error: 'File not found' });
    res.sendFile(filePath);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// Update spec status (drag-and-drop)
app.patch('/api/specs/:specId/status', async (req, res) => {
  try {
    const { status } = req.body;
    const validStatuses = ['초안', '대기', '작업', '테스트 검증', '리뷰', '검토', '활성', '완료'];
    if (!validStatuses.includes(status)) {
      return res.status(400).json({ error: `Invalid status: ${status}` });
    }

    const spec = await reader.getSpec(req.params.specId);
    if (!spec) return res.status(404).json({ error: 'Spec not found' });

    const oldStatus = spec.status;
    await writer.updateStatus(req.params.specId, status);

    // Reset attempt count when moving from 검토 to 대기
    if (oldStatus === '검토' && status === '대기') {
      await writer.resetAttemptCount(req.params.specId);
    }

    await logger.append(req.params.specId, {
      role: 'user',
      summary: `상태 변경: ${oldStatus} → ${status}`,
      statusChange: { from: oldStatus, to: status },
      result: 'handoff',
    });

    res.json({ ok: true });
  } catch (err) {
    console.error(`PATCH /api/specs/${req.params.specId}/status error:`, err);
    res.status(500).json({ error: err.message });
  }
});

// Answer a question
app.post('/api/specs/:specId/questions/:qId/answer', async (req, res) => {
  try {
    const { answer } = req.body;
    await writer.answerQuestion(req.params.specId, req.params.qId, answer);

    await logger.append(req.params.specId, {
      role: 'user',
      summary: `질문 ${req.params.qId} 답변 완료`,
      result: 'handoff',
    });

    res.json({ ok: true });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// Submit user test result
app.post('/api/specs/:specId/tests/:testId/result', async (req, res) => {
  try {
    const { result, comment } = req.body;
    if (!['pass', 'fail'].includes(result)) {
      return res.status(400).json({ error: 'Result must be "pass" or "fail"' });
    }

    await writer.submitTestResult(req.params.specId, req.params.testId, result, comment);

    await logger.append(req.params.specId, {
      role: 'user',
      summary: `사용자 테스트 ${req.params.testId}: ${result}${comment ? ' - ' + comment : ''}`,
      result: result === 'pass' ? 'verified' : 'requeue',
    });

    res.json({ ok: true });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// --- Runner API ---

app.get('/api/runner/status', (req, res) => {
  res.json(runner.getStatus());
});

app.post('/api/runner/start', async (req, res) => {
  try {
    const status = await runner.start();
    res.json(status);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

app.post('/api/runner/stop', (req, res) => {
  const status = runner.stop();
  res.json(status);
});

app.post('/api/runner/schedule-stop', (req, res) => {
  const status = runner.scheduleStop();
  res.json(status);
});

// SSE for live updates
const clients = new Set();
app.get('/api/events', (req, res) => {
  res.writeHead(200, {
    'Content-Type': 'text/event-stream',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive',
  });
  res.write('data: connected\n\n');
  clients.add(res);
  req.on('close', () => clients.delete(res));
});

function broadcast(event, data) {
  const msg = `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
  for (const client of clients) {
    client.write(msg);
  }
}

// Broadcast runner state changes via SSE
runner.events.on('state', (status) => {
  broadcast('runner', status);
});
runner.events.on('log', (entry) => {
  broadcast('runner-log', entry);
});

// Watch specs directory for changes
const fs = require('fs');
try {
  fs.watch(SPECS_DIR, { recursive: true }, (eventType, filename) => {
    broadcast('change', { eventType, filename });
  });
} catch {
  // Specs directory may not exist yet
}

app.listen(PORT, () => {
  console.log(`Flow Kanban running at http://localhost:${PORT}`);
  console.log(`Specs directory: ${SPECS_DIR}`);
});
