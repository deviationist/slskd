import ShrinkableButton from './ShrinkableButton';
import React from 'react';
import { Button, Dropdown } from 'semantic-ui-react';

const ShrinkableDropdownButton = ({
  children,
  color,
  disabled,
  hidden,
  icon,
  loading,
  mediaQuery,
  onChange,
  onClick,
  options,
}) => {
  if (hidden) {
    return null;
  }

  return (
    // the class carries the padding on the labelled button's caret side; see
    // `.shrinkable-dropdown` in App.css
    <Button.Group
      className="shrinkable-dropdown"
      color={color}
    >
      <ShrinkableButton
        disabled={disabled}
        icon={icon}
        loading={loading}
        mediaQuery={mediaQuery}
        onClick={onClick}
      >
        {children}
      </ShrinkableButton>
      <Dropdown
        className="button icon"
        disabled={disabled}
        onChange={onChange}
        options={options}
        trigger={null}
      />
    </Button.Group>
  );
};

export default ShrinkableDropdownButton;
