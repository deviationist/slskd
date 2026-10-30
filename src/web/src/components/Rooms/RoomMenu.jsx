import './Rooms.css';
import RoomJoinModal from './RoomJoinModal';
import { useRoomsUnread } from './RoomsUnreadContext';
import React from 'react';
import { Button, Icon, Menu } from 'semantic-ui-react';

const RoomMenu = ({ active, joined, onRoomChange, ...rest }) => {
  const names = [...joined];
  const isActive = (name) => active === name;
  const { countOf } = useRoomsUnread();

  return (
    <Menu
      className="room-menu"
      size="large"
    >
      {names.map((name) => (
        <Menu.Item
          active={isActive(name)}
          className={`menu-item ${isActive(name) ? 'menu-active' : ''}`}
          key={name}
          name={name}
          onClick={() => onRoomChange(name)}
        >
          <Icon
            color="green"
            name="circle"
            size="tiny"
          />
          {name}
          {!isActive(name) && countOf(name) > 0 && (
            <span
              className="room-menu-count"
              title={`${countOf(name)} unread`}
            >
              {countOf(name)}
            </span>
          )}
        </Menu.Item>
      ))}
      <Menu.Menu position="right">
        <RoomJoinModal
          centered
          size="small"
          trigger={
            <Button
              className="add-button"
              icon
            >
              <Icon name="plus" />
            </Button>
          }
          {...rest}
        />
      </Menu.Menu>
    </Menu>
  );
};

export default RoomMenu;
