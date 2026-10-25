import React from 'react';
import { List } from 'semantic-ui-react';

/**
 * The names of the files in a share: one on a line of its own, several as a
 * list of the first few with the rest counted. A list of one is a bullet with
 * nothing to set it apart from.
 * @param {object} params
 * @param {string} params.className - The class, for the margins either form keeps.
 * @param {string[]} params.names - The names to show.
 * @param {number} params.total - How many files there are, shown or not.
 */
const FileNames = ({ className, names, total }) => {
  if (total === 1) {
    return <p className={`${className} share-file-single`}>{names[0]}</p>;
  }

  return (
    <List
      bulleted
      className={className}
    >
      {names.map((name, index) => (
        // by position: two folders' '01 - Intro.flac' are the same name
        // eslint-disable-next-line react/no-array-index-key
        <List.Item key={index}>{name}</List.Item>
      ))}
      {total > names.length && (
        <List.Item>and {total - names.length} more</List.Item>
      )}
    </List>
  );
};

export default FileNames;
