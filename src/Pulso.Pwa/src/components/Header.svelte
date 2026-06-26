<script>
  import { online } from '../lib/stores.js';
</script>

<header>
  <div class="brand">PULSO</div>
  <div class="network-badge" class:online={$online}>
    <span>{$online ? 'Internet Activo (En Línea)' : 'Sin Internet (Trabajando sin conexión)'}</span>
  </div>
</header>
