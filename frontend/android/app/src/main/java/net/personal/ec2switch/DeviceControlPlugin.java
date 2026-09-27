package net.personal.ec2switch;

import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyInfo;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.KeyFactory;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.security.spec.ECGenParameterSpec;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONObject;

@CapacitorPlugin(name = "DeviceControl")
public class DeviceControlPlugin extends Plugin {
    private static final String ALIAS = "ec2-switch-device-v1";
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private SharedPreferences prefs() { return getContext().getSharedPreferences("device", 0); }
    private KeyStore keys() throws Exception {
        KeyStore ks = KeyStore.getInstance("AndroidKeyStore");
        ks.load(null);
        if (!ks.containsAlias(ALIAS)) {
            KeyPairGenerator generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore");
            generator.initialize(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_SIGN)
                .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256)
                // Intentionally no password, biometric, account, or per-use authentication prompt.
                .setUserAuthenticationRequired(false).build());
            generator.generateKeyPair();
        }
        return ks;
    }
    private static String hex(byte[] data) {
        StringBuilder out = new StringBuilder();
        for (byte b : data) out.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
        return out.toString();
    }
    private static String hash(byte[] data) throws Exception { return hex(MessageDigest.getInstance("SHA-256").digest(data)); }
    private static boolean validEndpoint(String endpoint) {
        return endpoint != null && endpoint.matches("https://[a-z0-9]{10}\\.execute-api\\.ap-southeast-5\\.amazonaws\\.com");
    }
    @PluginMethod public void info(PluginCall call) {
        executor.execute(() -> {
            try {
                KeyStore ks = keys();
                byte[] der = ks.getCertificate(ALIAS).getPublicKey().getEncoded();
                PrivateKey privateKey = (PrivateKey) ks.getKey(ALIAS, null);
                KeyInfo keyInfo = KeyFactory.getInstance(privateKey.getAlgorithm(), "AndroidKeyStore").getKeySpec(privateKey, KeyInfo.class);
                JSObject result = new JSObject();
                result.put("deviceId", hash(der));
                result.put("publicKey", Base64.encodeToString(der, Base64.NO_WRAP));
                result.put("endpoint", prefs().getString("endpoint", ""));
                result.put("hardwareBacked", keyInfo.isInsideSecureHardware());
                call.resolve(result);
            } catch (Exception e) { call.reject("KEY_UNAVAILABLE"); }
        });
    }
    @PluginMethod public void configure(PluginCall call) {
        String endpoint = call.getString("endpoint", "").replaceAll("/+$", "");
        if (!validEndpoint(endpoint)) { call.reject("INVALID_ENDPOINT"); return; }
        prefs().edit().putString("endpoint", endpoint).apply();
        call.resolve();
    }
    @PluginMethod public void request(PluginCall call) {
        final String action = call.getString("action", "");
        if (!action.equals("status") && !action.equals("start") && !action.equals("stop")) { call.reject("BAD_ACTION"); return; }
        executor.execute(() -> {
            HttpsURLConnection connection = null;
            try {
                String endpoint = prefs().getString("endpoint", "");
                if (!validEndpoint(endpoint)) { call.reject("NOT_CONFIGURED"); return; }
                KeyStore ks = keys();
                String id = hash(ks.getCertificate(ALIAS).getPublicKey().getEncoded());
                String timestamp = Long.toString(System.currentTimeMillis() / 1000L);
                byte[] random = new byte[16]; new SecureRandom().nextBytes(random);
                String nonce = hex(random);
                byte[] body = ("{\"action\":\"" + action + "\"}").getBytes(StandardCharsets.UTF_8);
                String canonical = String.join("\n", "EC2SWITCH1", endpoint, "POST", "/control", id, timestamp, nonce, hash(body));
                Signature signer = Signature.getInstance("SHA256withECDSA");
                signer.initSign((PrivateKey) ks.getKey(ALIAS, null));
                signer.update(canonical.getBytes(StandardCharsets.UTF_8));
                String signature = Base64.encodeToString(signer.sign(), Base64.NO_WRAP);
                connection = (HttpsURLConnection) new URL(endpoint + "/control").openConnection();
                connection.setRequestMethod("POST");
                connection.setInstanceFollowRedirects(false);
                connection.setConnectTimeout(10000); connection.setReadTimeout(20000);
                connection.setUseCaches(false); connection.setDoOutput(true);
                connection.setFixedLengthStreamingMode(body.length);
                connection.setRequestProperty("Content-Type", "application/json");
                connection.setRequestProperty("x-device-id", id);
                connection.setRequestProperty("x-timestamp", timestamp);
                connection.setRequestProperty("x-nonce", nonce);
                connection.setRequestProperty("x-signature", signature);
                try (java.io.OutputStream output = connection.getOutputStream()) { output.write(body); }
                int status = connection.getResponseCode();
                InputStream stream = status >= 400 ? connection.getErrorStream() : connection.getInputStream();
                ByteArrayOutputStream response = new ByteArrayOutputStream();
                if (stream != null) try (InputStream input = stream) {
                    byte[] buffer = new byte[1024]; int count;
                    while ((count = input.read(buffer)) != -1) {
                        if (response.size() + count > 8192) throw new java.io.IOException("Response too large");
                        response.write(buffer, 0, count);
                    }
                }
                JSObject result = new JSObject(); result.put("status", status);
                if (status == 200) {
                    JSONObject json = new JSONObject(response.toString("UTF-8"));
                    result.put("state", json.getString("state"));
                    result.put("observedAt", json.getLong("observedAt"));
                    if (json.has("dns")) result.put("dns", json.getJSONObject("dns"));
                } else {
                    String error = status == 429 ? "THROTTLED" : "SERVICE_UNAVAILABLE";
                    try { error = new JSONObject(response.toString("UTF-8")).optString("error", error); } catch (Exception ignored) { }
                    result.put("error", error);
                }
                call.resolve(result);
            } catch (Exception e) { call.reject("NETWORK"); }
            finally { if (connection != null) connection.disconnect(); }
        });
    }
    @Override protected void handleOnDestroy() { executor.shutdownNow(); }
}
