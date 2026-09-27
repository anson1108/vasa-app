package net.personal.ec2switch;

import com.getcapacitor.BridgeActivity;
import android.os.Bundle;

public class MainActivity extends BridgeActivity {
    @Override public void onCreate(Bundle state) {
        registerPlugin(DeviceControlPlugin.class);
        super.onCreate(state);
    }
}
