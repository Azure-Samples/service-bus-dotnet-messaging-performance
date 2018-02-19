//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    using System;

    public class Observable<T>
    {
        public class ChangingEventArgs : EventArgs
        {
            public T OldValue { get; private set; }
            public T NewValue { get; private set; }
            public bool Cancel { get; set; }

            public ChangingEventArgs(T oldValue, T newValue)
            {
                OldValue = oldValue;
                NewValue = newValue;
                Cancel = false;
            }
        }

        public class ChangedEventArgs : EventArgs
        {
            public T Value { get; private set; }

            public ChangedEventArgs(T value)
            {
                Value = value;
            }
        }

        public event EventHandler<ChangingEventArgs> Changing;
        public event EventHandler<ChangedEventArgs> Changed;

        protected T value;

        public T Value
        {
            get
            {
                return value;
            }
            set
            {
                if (this.value.Equals(value))
                {
                    return;
                }
                ChangingEventArgs e = new ChangingEventArgs(this.value, value);
                OnChanging(e);
                if (e.Cancel)
                {
                    return;
                }
                this.value = value;
                OnChanged(new ChangedEventArgs(this.value));
            }
        }

        public Observable() { }

        public Observable(T value)
        {
            this.value = value;
        }

        protected virtual void OnChanging(ChangingEventArgs e)
        {
            EventHandler<ChangingEventArgs> handler = Changing;
            if (handler == null)
            {
                return;
            }
            handler(this, e);
        }

        protected virtual void OnChanged(ChangedEventArgs e)
        {
            EventHandler<ChangedEventArgs> handler = Changed;
            if (handler == null)
            {
                return;
            }
            handler(this, e);
        }
    }
}
