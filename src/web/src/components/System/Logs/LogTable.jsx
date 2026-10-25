import '../System.css';
import { Timestamp } from '../../Shared';
import React from 'react';
import { Table } from 'semantic-ui-react';

const abbreviations = {
  Debug: 'DBG',
  Error: 'ERR',
  Fatal: 'FTL',
  Information: 'INF',
  Verbose: 'VRB',
  Warning: 'WRN',
};

const LogTable = ({ emptyMessage = 'No logs', logs }) => (
  <Table
    className="logs-table"
    compact="very"
  >
    <Table.Header>
      <Table.Row>
        <Table.HeaderCell>Timestamp</Table.HeaderCell>
        <Table.HeaderCell>Level</Table.HeaderCell>
        <Table.HeaderCell>Message</Table.HeaderCell>
      </Table.Row>
    </Table.Header>
    <Table.Body className="logs-table-body">
      {logs?.length === 0 ? (
        <Table.Row>
          <Table.Cell
            colSpan={99}
            style={{
              opacity: 0.5,
              padding: '10px !important',
              textAlign: 'center',
            }}
          >
            {emptyMessage}
          </Table.Cell>
        </Table.Row>
      ) : (
        logs.map((log) => (
          <Table.Row
            disabled={log.level === 'Debug' || log.level === 'Verbose'}
            key={log.id}
            negative={log.level === 'Error' || log.level === 'Fatal'}
            warning={log.level === 'Warning'}
          >
            <Table.Cell>
              {/* the fork's one way of drawing a date: in the configured
                  locale and zone, and with the day once it is not today --
                  which History, reading old log files, needs */}
              <Timestamp at={log.timestamp} />
            </Table.Cell>
            <Table.Cell>{abbreviations[log.level] || log.level}</Table.Cell>
            <Table.Cell className="logs-table-message">
              {log.message}
            </Table.Cell>
          </Table.Row>
        ))
      )}
    </Table.Body>
  </Table>
);

export default LogTable;
